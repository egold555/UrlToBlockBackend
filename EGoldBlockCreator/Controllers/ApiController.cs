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
        public string GetUrl(string uuid)
        {
            Guid guid;
            
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                CloudBlockBlob blob = GetBlob(guid);
                return blob.Uri.ToString();
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }
        /*
        public string CreateBlob()
        {
            // Retrieve reference to a blob named "blob66".
            CloudBlockBlob blockBlob = BlobContainer.GetBlockBlobReference("blob66");

            // Create or overwrite the "myblob" blob with contents from a local file.
            using (var fileStream = System.IO.File.OpenRead(Server.MapPath("~/App_Data/MyTextFile64.txt")))
            {
                blockBlob.UploadFromStream(fileStream);
            }

            return blockBlob.Uri.ToString();
        }
        */

        CloudBlobContainer blobContainer = null;

        CloudBlockBlob GetBlob(Guid guid)
        {
            CloudBlockBlob blockBlob = BlobContainer.GetBlockBlobReference("rp-" + guid.ToString() + ".zip");

            if (! blockBlob.Exists())
            {
                // Create or overwrite the "myblob" blob with contents from a local file.
                using (var fileStream = System.IO.File.OpenRead(Server.MapPath("~/App_Data/DefaultResourcePack.zip")))
                {
                    blockBlob.UploadFromStream(fileStream);
                }
            }

            return blockBlob;
        }

        CloudBlobContainer BlobContainer
        {
            get {
                if (blobContainer == null)
                {
                    // Retrieve storage account from connection string.
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                        CloudConfigurationManager.GetSetting("STORAGE"));

                    // Create the blob client.
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                    // Retrieve reference to a previously created container.
                    blobContainer = blobClient.GetContainerReference("blockcreator");
                }

                return blobContainer;
            }
        }
    }
}