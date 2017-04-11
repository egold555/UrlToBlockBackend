using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EGoldBlockCreator.Controllers
{
    public class ApiController : Controller
    {
        // GET: Api
        public string TestIt()
        {
            return "HELLO WORLD";
        }

        public string CreateBlob()
        {
            // Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("STORAGE"));

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve reference to a previously created container.
            CloudBlobContainer container = blobClient.GetContainerReference("blockcreator");

            // Retrieve reference to a blob named "myblob".
            CloudBlockBlob blockBlob = container.GetBlockBlobReference("blob66");

            // Create or overwrite the "myblob" blob with contents from a local file.
            using (var fileStream = System.IO.File.OpenRead(Server.MapPath("~/App_Data/MyTextFile64.txt")))
            {
                blockBlob.UploadFromStream(fileStream);
            }

            return blockBlob.Uri.ToString();
        }
    }
}