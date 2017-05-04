using Ionic.Zip;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

        public string GetUrl(string uuid, bool spawner, string merge)
        {
            Guid guid;
            
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                CloudBlockBlob blob = UpdateZipToSecondary(guid, (zipFile) => 
                {
                    bool update;

                    if (spawner)
                    {
                        // We want the file in there.
                        if (!zipFile.ContainsEntry(MobSpawnerCageName()))
                        {
                            zipFile.AddEntry(MobSpawnerCageName(), System.IO.File.ReadAllBytes(Server.MapPath("~/App_Data/mob_spawner_cage.json")));
                            update = true;
                        }
                        else
                        {
                            update = false;
                        }
                    }
                    else
                    {
                        // We don't want the file in there.
                        if (zipFile.ContainsEntry(MobSpawnerCageName()))
                        {
                            zipFile.RemoveEntry(MobSpawnerCageName());
                            update = true;
                        }
                        else
                        {
                            update = false;
                        }
                    }

                    if (!string.IsNullOrEmpty(merge)) {
                        byte[] externalZipData = DownloadExternalTexturePack(merge);
                        if (externalZipData != null) {
                            using (ZipFile externalZip = ZipFile.Read(new MemoryStream(externalZipData))) {
                                MergeZip(zipFile, externalZip);
                                update = true;
                            }
                        }
                    }

                    return update;
                });

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

        public string DeleteAll(string uuid)
        {
            Guid guid;

            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                DeleteBlob(guid);
                return "OK";
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

                int numFrames;

                try {
                    string newTextureFile = TempFile(".png");

                    using (Bitmap bitmap = (Bitmap) Image.FromFile(textureFile))
                    {
                       
                        Bitmap newBitmap = RescaleImage(bitmap, GetBestSize(bitmap.Size), out numFrames);
                        newBitmap.Save(newTextureFile);
                    }

                    System.IO.File.Delete(textureFile);
                    textureFile = newTextureFile;
                }
                catch (Exception)
                {
                    return ("@FAILURE: The image file appears to be invalid.");
                }

                int damage = -1;

                error = null;

                using (Stream stm = new FileStream(textureFile, FileMode.Open, FileAccess.Read))
                {
                    UpdateZip(guid, (ZipFile zipFile) => {
                        damage = FirstFreeDamage(zipFile);
                        if (damage < 0)
                        {
                            error = "Maximum number of blocks reached";
                            return false;
                        }

                        string textureName = TextureName(damage);
                        zipFile.AddEntry(textureName, stm);

                        if (numFrames > 1) {
                            string animatedTextureName = AnimatedTextureName(damage);
                            byte[] content = GetMcMeta(numFrames);

                            zipFile.AddEntry(animatedTextureName, content);
                        }

                        string modelName = ModelName(damage);
                        string modelContent = System.IO.File.ReadAllText(Server.MapPath("~/App_Data/default_model.json"), Encoding.UTF8);
                        modelContent = modelContent.Replace("@DAMAGE@", (damage - 1).ToString());
                        zipFile.AddEntry(modelName, modelContent, Encoding.UTF8);

                        string handModelName = HandModelName(damage);
                        string handModelContent = System.IO.File.ReadAllText(Server.MapPath("~/App_Data/hand_default_model.json"), Encoding.UTF8);
                        handModelContent = handModelContent.Replace("@DAMAGE@", (damage - 1).ToString());
                        zipFile.AddEntry(handModelName, handModelContent, Encoding.UTF8);

                        UpdateHoeModel(zipFile);
                        UpdateAxeModel(zipFile);

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

        public string DeleteTexture(string uuid, int damage)
        {
            Guid guid;

            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                UpdateZip(guid, (ZipFile zipFile) => {
                    string textureName = TextureName(damage);
                    string animatedTextureName = AnimatedTextureName(damage);
                    string modelName = ModelName(damage);
                    string handModelName = HandModelName(damage);

                    if (zipFile.ContainsEntry(textureName))
                        zipFile.RemoveEntry(textureName);
                    if (zipFile.ContainsEntry(animatedTextureName))
                        zipFile.RemoveEntry(animatedTextureName);
                    if (zipFile.ContainsEntry(modelName))
                        zipFile.RemoveEntry(modelName);
                    if (zipFile.ContainsEntry(handModelName))
                        zipFile.RemoveEntry(handModelName);

                    UpdateHoeModel(zipFile);
                    UpdateAxeModel(zipFile);

                    return true;
                });

                return "OK";
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        Size GetBestSize(Size existing)
        {
            int size;
            int biggest;

            if (existing.Height < existing.Width * 2)
                biggest = Math.Max(existing.Width, existing.Height);
            else
                biggest = existing.Width;

            if (biggest >= 256)
                size = 256;
            else if (biggest >= 128)
                size = 128;
            else if (biggest >= 64)
                size = 64;
            else if (biggest >= 32)
                size = 32;
            else
                size = 16;

            return new Size(size, size);
        }

        Bitmap RescaleImage(Bitmap source, Size size, out int numFrames)
        {
            numFrames = 1;
            try {
                numFrames = source.GetFrameCount(FrameDimension.Time);
            }
            catch (Exception) { }

            if (numFrames > 1)
            {
                var bmp = new Bitmap(size.Width, size.Height * numFrames, PixelFormat.Format32bppArgb);

                using (var gr = Graphics.FromImage(bmp)) {
                    gr.Clear(Color.Transparent);
                    gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    for (int frame = 0; frame < numFrames; ++frame) {
                        source.SelectActiveFrame(FrameDimension.Time, frame);
                        gr.DrawImage(source, new Rectangle(0, size.Height * frame, size.Width, size.Height));
                    }
                }

                return bmp;
            }
            
            if (source.Height >= source.Width * 2 && source.Height % source.Width == 0) {
                // Minecraft-style animated texture.

                numFrames = source.Height / source.Width;

                // 1st bullet, pixel format
                var bmp = new Bitmap(size.Width, size.Height * numFrames, PixelFormat.Format32bppArgb);

                // 2nd bullet, resolution
                using (var gr = Graphics.FromImage(bmp)) {
                    // 3rd bullet, background
                    gr.Clear(Color.Transparent);
                    // 4th bullet, interpolation
                    gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    gr.DrawImage(source, new Rectangle(0, 0, size.Width, size.Height * numFrames));
                }

                return bmp;
            }
            else 
            {
                // 1st bullet, pixel format
                var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);

                // 2nd bullet, resolution
                using (var gr = Graphics.FromImage(bmp))
                {
                    // 3rd bullet, background
                    gr.Clear(Color.Transparent);
                    // 4th bullet, interpolation
                    gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    gr.DrawImage(source, new Rectangle(0, 0, size.Width, size.Height));
                }

                return bmp;
            }
        }

        private byte[] DownloadExternalTexturePack(string urlExternalTexturePack)
        {
            try {
                WebClient webClient = new WebClient();
                webClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.Default);
                return webClient.DownloadData(urlExternalTexturePack);
            }
            catch (Exception e) {
                return null;
            }
        }

        // Merge any entries from "mergeFrom" into "zipFile". Will not overwrite entries in the 
        // existing zipFile.
        private void MergeZip(ZipFile zipFile, ZipFile mergeFrom)
        {
            foreach (ZipEntry entry in mergeFrom) {
                string fileName = entry.FileName;
                if (! zipFile.ContainsEntry(fileName)) {
                    // Copy the entry into the zipFile.
                    MemoryStream stream = new MemoryStream();
                    entry.Extract(stream);
                    zipFile.AddEntry(fileName, stream.GetBuffer());
                }
            }
        }

        private string ReadTexture(string textureUrl, out string error)
        {
            string fileName = TempFile(Path.GetExtension(new Uri(textureUrl).AbsolutePath));

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

        void UpdateHoeModel(ZipFile zipFile)
        {
            List<int> usedDamages = UsedDamages(zipFile);
            byte[] content = GetHoeModel(usedDamages);
            zipFile.RemoveEntry(@"/assets/minecraft/models/item/diamond_hoe.json");
            zipFile.AddEntry(@"/assets/minecraft/models/item/diamond_hoe.json", content);
        }

        void UpdateAxeModel(ZipFile zipFile)
        {
            List<int> usedDamages = UsedDamages(zipFile);
            byte[] content = GetAxeModel(usedDamages);
            zipFile.RemoveEntry(@"/assets/minecraft/models/item/diamond_axe.json");
            zipFile.AddEntry(@"/assets/minecraft/models/item/diamond_axe.json", content);
        }

        byte[] GetHoeModel(List<int> usedDamages)
        {
            MemoryStream stream = new MemoryStream();
            TextWriter writer = new StreamWriter(stream);

            writer.WriteLine(@"{");
            writer.WriteLine(@"	""parent"": ""item/handheld"",");
            writer.WriteLine(@"	""textures"": {");
            writer.WriteLine(@"		""layer0"": ""items/diamond_hoe""");
            writer.WriteLine(@"	},");
            writer.WriteLine(@"	""overrides"": [");
            writer.WriteLine(@"		{ ""predicate"": {""damaged"": 0, ""damage"": 0}, ""model"": ""item/diamond_hoe""},");
            writer.WriteLine();

            foreach (int d in usedDamages)
            {
                string s = @"		{ ""predicate"": {""damaged"": 0, ""damage"": @DMG@}, ""model"": ""block/customblock_@BLK@""},";
                s = s.Replace("@DMG@", ((double)d / 1562.0).ToString("R")).Replace("@BLK@", (d - 1).ToString());
                writer.WriteLine(s);
            }

            writer.WriteLine();
            writer.WriteLine(@"		{ ""predicate"": {""damaged"": 1, ""damage"": 0}, ""model"": ""item/diamond_hoe""}");
            writer.WriteLine(@"	]");
            writer.WriteLine(@"}");

            writer.Flush();

            return stream.ToArray();
        }

        byte[] GetAxeModel(List<int> usedDamages)
        {
            MemoryStream stream = new MemoryStream();
            TextWriter writer = new StreamWriter(stream);

            writer.WriteLine(@"{");
            writer.WriteLine(@"	""parent"": ""item/handheld"",");
            writer.WriteLine(@"	""textures"": {");
            writer.WriteLine(@"		""layer0"": ""items/diamond_axe""");
            writer.WriteLine(@"	},");
            writer.WriteLine(@"	""overrides"": [");
            writer.WriteLine(@"		{ ""predicate"": {""damaged"": 0, ""damage"": 0}, ""model"": ""item/diamond_axe""},");
            writer.WriteLine();

            foreach (int d in usedDamages)
            {
                string s = @"		{ ""predicate"": {""damaged"": 0, ""damage"": @DMG@}, ""model"": ""block/hand_customblock_@BLK@""},";
                s = s.Replace("@DMG@", ((double)d / 1562.0).ToString("R")).Replace("@BLK@", (d - 1).ToString());
                writer.WriteLine(s);
            }

            writer.WriteLine();
            writer.WriteLine(@"		{ ""predicate"": {""damaged"": 1, ""damage"": 0}, ""model"": ""item/diamond_axe""}");
            writer.WriteLine(@"	]");
            writer.WriteLine(@"}");

            writer.Flush();

            return stream.ToArray();
        }

        byte[] GetMcMeta(int numFrames)
        {
            MemoryStream stream = new MemoryStream();
            TextWriter writer = new StreamWriter(stream);

            writer.WriteLine(@"{");
            writer.WriteLine(@"    ""animation"": {");
            writer.WriteLine(@"        ""frames"": [");

            for (int i = 0; i < numFrames; ++i) {
                writer.Write(@"{ ""index"": " + i.ToString() + @", ""time"": 1}");
                if (i != numFrames - 1)
                    writer.Write(@",");
                writer.WriteLine();
            }

            writer.WriteLine(@"        ]");
            writer.WriteLine(@"    }");
            writer.WriteLine(@"}");

            writer.Flush();

            return stream.ToArray();
        }

        List<int> UsedDamages(Guid guid)
        {
            List<int> used = new List<int>();

            UpdateZip(guid, (ZipFile zipFile) =>
            {
                used = UsedDamages(zipFile);
                return false;
            });

            return used;
        }

        List<int> UsedDamages(ZipFile zipFile)
        {
            List<int> used = new List<int>();

            for (int d = 1; d <= maxDamage; ++d)
            {
                if (DamageExists(zipFile, d))
                    used.Add(d);
            }

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

        string AnimatedTextureName(int d)
        {
            return TexturesFolder() + "/customblock_" + (d - 1).ToString() + ".png.mcmeta";
        }

        string ModelName(int d)
        {
            return ModelsFolder() + "/customblock_" + (d-1).ToString() + ".json";
        }

        string HandModelName(int d)
        {
            return ModelsFolder() + "/hand_customblock_" + (d - 1).ToString() + ".json";
        }

        string MobSpawnerCageName()
        {
            return ModelsFolder() + "/mob_spawner_cage.json";
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
            string tempPath = TempFile(".zip");
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

        CloudBlockBlob UpdateZipToSecondary(Guid guid, Func<ZipFile, bool> processZip)
        {
            string tempPath = TempFile(".zip");
            CloudBlockBlob blob = GetBlob(guid);
            blob.DownloadToFile(tempPath, FileMode.Create);
            ZipFile zipFile = ZipFile.Read(tempPath);
            bool changed = processZip(zipFile);
            zipFile.Save();
            zipFile.Dispose();
            if (changed) {
                blob = GetSecondaryBlob(guid);
                blob.UploadFromFile(tempPath);
            }
            System.IO.File.Delete(tempPath);

            return blob;
        }

        void DeleteBlob(Guid guid)
        {
            CloudBlockBlob blockBlob = BlobContainer.GetBlockBlobReference("rp-" + guid.ToString() + ".zip");

            if (blockBlob.Exists())
            {
                blockBlob.Delete();
            }
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

        CloudBlockBlob GetSecondaryBlob(Guid guid)
        {
            CloudBlockBlob blockBlob = BlobContainer.GetBlockBlobReference("rpx-" + guid.ToString() + ".zip");
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

        string TempFile(string extension)
        {
            return Path.ChangeExtension(System.IO.Path.GetTempPath() + Guid.NewGuid().ToString(), extension);
        }
    }
}