using Ionic.Zip;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace EGoldBlockCreator.Controllers
{
    public class ApiController : Controller
    {
        CloudBlobContainer blobContainer = null;
        const int maxDamage = 1559;

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

        public string GetTextures(string uuid)
        {
            Guid guid;
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                List<int> used = UsedDamages(guid);
                StringBuilder builder = new StringBuilder();
                bool first = true;
                foreach (int d in used)
                {
                    if (!first)
                        builder.Append(',');
                    builder.Append(d.ToString());
                    first = false;
                }

                return builder.ToString();
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        public string AddTexture(string uuid, string texture)
        {
            Guid guid;

            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                string error = null;
                Uri url = new Uri(texture);
                string textureFile = ReadTexture(texture, out error);
                if (textureFile == null)
                {
                    return "@FAILURE: Could not read texture: " + error;
                }

                int damage = -1;

                error = null;

                using (Stream stm = new FileStream(textureFile, FileMode.Open, FileAccess.Read))
                {
                    UpdateZip(guid, (ZipFile zipFile) => {
                        damage = FirstFreeDamage(zipFile);
                        if (damage < 0)
                        {
                            error = "No free textures";
                            return false;
                        }

                        string textureName = TextureName(damage);
                        zipFile.AddEntry(textureName, stm);

                        string modelName = ModelName(damage);
                        string modelContent = System.IO.File.ReadAllText(Server.MapPath("~/App_Data/default_model.json"), Encoding.UTF8);
                        modelContent = modelContent.Replace("@DAMAGE@", (damage - 1).ToString());
                        zipFile.AddEntry(modelName, modelContent, Encoding.UTF8);

                        return true;
                    });
                }

                System.IO.File.Delete(textureFile);

                if (string.IsNullOrEmpty(error))
                {
                    return damage.ToString();
                }
                else
                {
                    return "@FAILURE: " + error;
                }
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        private string ReadTexture(string textureUrl, out string error)
        {
            string fileName = Path.GetTempFileName();

            error = null;
            try
            {
                WebClient webClient = new WebClient();
                webClient.DownloadFile(textureUrl, fileName);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            return fileName;
        }

        List<int> UsedDamages(Guid guid)
        {
            List<int> used = new List<int>();

            UpdateZip(guid, (ZipFile zipFile) =>
            {
                for (int d = 1; d <= maxDamage; ++d)
                {
                    if (DamageExists(zipFile, d))
                        used.Add(d);
                }
                return false;
            });

            return used;
        }

        int FirstFreeDamage(ZipFile zipFile)
        {
            for (int d = 1; d <= maxDamage; ++d)
            {
                if (!DamageExists(zipFile, d))
                    return d;
            }
            return -1;
        }

        private bool DamageExists(ZipFile zipFile, int d)
        {
            return zipFile.ContainsEntry(TextureName(d));
        }

        string TextureName(int d)
        {
            return TexturesFolder() + "/customblock_" + (d-1).ToString() + ".png";
        }

        string ModelName(int d)
        {
            return ModelsFolder() + "/customblock_" + (d-1).ToString() + ".json";
        }

        string TexturesFolder()
        {
            return "assets/minecraft/textures/blocks";
        }

        string ModelsFolder()
        {
            return "assets/minecraft/models/block";
        }



        void UpdateZip(Guid guid, Func<ZipFile, bool> processZip)
        {
            string tempPath = Path.GetTempFileName();
            CloudBlockBlob blob = GetBlob(guid);
            blob.DownloadToFile(tempPath, FileMode.Create);
            ZipFile zipFile = ZipFile.Read(tempPath);
            bool changed = processZip(zipFile);
            zipFile.Save();
            zipFile.Dispose();
            if (changed)
            {
                blob.UploadFromFile(tempPath);
            }
            System.IO.File.Delete(tempPath);
        }

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