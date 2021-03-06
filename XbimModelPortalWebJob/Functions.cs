﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Storage.Blob;
using XbimCloudCommon;
using Microsoft.Azure.WebJobs;
using Xbim.IO;
using XbimGeometry.Interfaces;
using Xbim.ModelGeometry.Scene;
using Xbim.COBieLite;
using Xbim.DPoW.Interfaces;

namespace XbimModelPortalWebJob
{
    public class Functions
    {
        // This function will get triggered/executed when a new message is written 
        // on an Azure Queue called queue.
        //public static void ProcessQueueMessage([QueueTrigger("queue")] string message, TextWriter log)
        //{
        //    log.WriteLine(message);
        //}

        public static void GenerateModel(
            [QueueTrigger("modelrequest")] XbimCloudModel blobInfo,
            [Blob("images/{ModelId}{Extension}", FileAccess.Read)] Stream input,
            [Blob("images/{ModelId}.wexBIM")] CloudBlockBlob outputWexbimBlob,
            [Blob("images/{ModelId}.json")] CloudBlockBlob outputCobieBlob)
        {

            if (input != null)
            {
                ConvertIfcToWexbimAndCobie(input, outputWexbimBlob, outputCobieBlob, blobInfo.Extension);
                outputWexbimBlob.Properties.ContentType = ".wexBIM";
                outputCobieBlob.Properties.ContentType = ".json";
            }
        }

        public static void GenerateDpow(
            [QueueTrigger("dpowrequest")] XbimCloudModel blobInfo,
            [Blob("images/{ModelId}{Extension}", FileAccess.Read)] Stream input,
            [Blob("images/{ModelId}.json")] CloudBlockBlob outputCobieBlob)
        {
            if (input != null)
            {
                ConvertDpowToCobie(input, outputCobieBlob);
                outputCobieBlob.Properties.ContentType = ".json";
            }
        }


        public static void ConvertDpowToCobie(Stream input, CloudBlockBlob outputCobieBlob)
        {
            var temp = Path.GetTempFileName();
            try
            {
                var dpow = PlanOfWork.Open(input);
                var facility = new FacilityType();
                var exchanger = new XbimExchanger.DPoWToCOBieLite.DpoWtoCoBieLiteExchanger(dpow, facility);
                exchanger.Convert();

                using (var tw = File.CreateText(temp))
                {
                    CoBieLiteHelper.WriteJson(tw, facility);
                    tw.Close();
                }
                outputCobieBlob.UploadFromFile(temp, FileMode.Open);
            }
            finally
            {
                //tidy up
                if (File.Exists(temp)) File.Delete(temp);
            }
           
        }

        public static void ConvertIfcToWexbimAndCobie(Stream input, CloudBlockBlob outputWexbimBlob, CloudBlockBlob outputCobieBlob, string inputExtension)
        {
            //temp files 
            var fileName = Path.GetTempPath() + Guid.NewGuid() + inputExtension;
            var xbimFileName = Path.ChangeExtension(fileName, ".xBIM");
            var wexBimFileName = Path.ChangeExtension(fileName, ".wexBIM");
            var cobieFileName = Path.ChangeExtension(fileName, ".json");
            try
            {

                using (var fileStream = File.OpenWrite(fileName))
                {
                    input.CopyTo(fileStream);
                    fileStream.Flush();
                    fileStream.Close();
                    //open the model and import
                    using (var model = new XbimModel())
                    {
                        model.CreateFrom(fileName, null, null, true);
                        var m3DModelContext = new Xbim3DModelContext(model);

                        using (var wexBimFile = new FileStream(wexBimFileName, FileMode.Create))
                        {
                            using (var bw = new BinaryWriter(wexBimFile))
                            {
                                m3DModelContext.CreateContext(XbimGeometryType.PolyhedronBinary);
                                m3DModelContext.Write(bw);
                                bw.Close();
                                wexBimFile.Close();
                                outputWexbimBlob.UploadFromFile(wexBimFileName, FileMode.Open);
                            }
                        }

                        using (var cobieFile = new FileStream(cobieFileName, FileMode.Create))
                        {
                            var helper = new CoBieLiteHelper(model, "UniClass");
                            var facility = helper.GetFacilities().FirstOrDefault();
                            if (facility != null)
                            {
                                using (var writer = new StreamWriter(cobieFile))
                                {
                                    CoBieLiteHelper.WriteJson(writer, facility);
                                    writer.Close();
                                    outputCobieBlob.UploadFromFile(cobieFileName, FileMode.Open);
                                }
                            }
                        }

                        model.Close();
                    }
                }
            }
            finally
            {
                //tidy up
                if (File.Exists(fileName)) File.Delete(fileName);
                if (File.Exists(xbimFileName)) File.Delete(xbimFileName);
                if (File.Exists(wexBimFileName)) File.Delete(wexBimFileName);
                if (File.Exists(cobieFileName)) File.Delete(cobieFileName);
            }
        }

    }
}
