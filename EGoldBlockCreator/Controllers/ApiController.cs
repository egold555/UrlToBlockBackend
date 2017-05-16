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
        const string packMcMeta = "pack.mcmeta";

        public string GetUrl(string uuid, bool spawner, bool? transparent, string merge)
        {
            Guid guid;
            
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                CloudBlockBlob blob = UpdateZip(guid, (zipFile) => 
                {
                    UpdateType updateType = UpdateType.None;

                    string mobCageFileName;
                    if (transparent.HasValue && transparent.Value) {
                        if (spawner) {
                            mobCageFileName = "mob_spawner_cage_transparent_noparticle.json";
                        }
                        else {
                            mobCageFileName = "mob_spawner_cage_transparent.json";
                        }
                    }
                    else {
                        if (spawner) {
                            mobCageFileName = "mob_spawner_cage_noparticle.json";
                        }
                        else {
                            mobCageFileName = "mob_spawner_cage.json";
                        }
                    }

                    if (UpdateZipEntryWithFile(zipFile, MobSpawnerCageName(), mobCageFileName)) {
                        updateType = UpdateType.Primary;
                    }

                    if (UpdateZipEntryWithFile(zipFile, CustomBlockName(), "custom_block.json")) {
                        updateType = UpdateType.Primary;
                    }

                    if (UpdateZipEntryWithFile(zipFile, SmallCubeName(), "small_cube.json")) {
                        updateType = UpdateType.Primary;
                    }

                    if (UpdateZipEntryWithFile(zipFile, packMcMeta, "pack.mcmeta")) {
                        updateType = UpdateType.Primary;
                    }

                    if (!string.IsNullOrEmpty(merge)) {
                        byte[] externalZipData = DownloadExternalTexturePack(merge);
                        if (externalZipData != null) {
                            using (ZipFile externalZip = ZipFile.Read(new MemoryStream(externalZipData))) {
                                MergeZip(zipFile, externalZip);
                                updateType = UpdateType.Secondary;
                            }
                        }
                    }

                    return updateType;
                });

                return blob.Uri.ToString();
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        public string GetTexturesAndNames(string uuid)
        {
            Guid guid;
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                List<int> used = null;
                Dictionary<int, NameAndLore> namesAndLore = null;

                UpdateZip(guid, (ZipFile zipFile) =>
                {
                    used = UsedDamages(zipFile);
                    namesAndLore = ReadNameAndLore(zipFile);
                    return UpdateType.None;
                });

                StringBuilder builder = new StringBuilder();
                foreach (int damage in used) {
                    if (namesAndLore.ContainsKey(damage)) {
                        builder.Append(namesAndLore[damage].ToLine());
                    }
                    else {
                        builder.Append(damage.ToString() + "&&");
                    }
                    builder.Append("|");
                }

                return builder.ToString();
            }
            else
            {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        public string GetTextures(string uuid)
        {
            Guid guid;
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid)) {
                List<int> used = UsedDamages(guid);
                return IntListToString(used);
            }
            else {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        public string Rename(string uuid, int damage, string name)
        {
            Guid guid;
            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid)) {
                Dictionary<int, NameAndLore> namesAndLore = null;

                UpdateZip(guid, (ZipFile zipFile) => {
                    namesAndLore = ReadNameAndLore(zipFile);
                    NameAndLore newNameAndLore;
                    if (namesAndLore.ContainsKey(damage)) {
                        newNameAndLore = new NameAndLore(damage, name, namesAndLore[damage].Lore);
                    }
                    else {
                        newNameAndLore = new NameAndLore(damage, name, "");
                    }

                    namesAndLore[damage] = newNameAndLore;
                    return UpdateType.Primary;
                });

                return "OK";
            }
            else {
                return "@FAILURE: uuid is not in a recognized format";
            }
        }

        private string IntListToString(List<int> l)
        {
            StringBuilder builder = new StringBuilder();
            bool first = true;
            foreach (int d in l) {
                if (!first)
                    builder.Append(',');
                builder.Append(d.ToString());
                first = false;
            }

            return builder.ToString();
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

        public string AddTiled(string uuid, string texture, int width, int height, string name)
        {
            return AddTextureWorker(uuid, texture, width, height, name);
        }

        public string AddTexture(string uuid, string texture, string name)
        {
            return AddTextureWorker(uuid, texture, 1, 1, name);
        }

        public string AddTextureWorker(string uuid, string texture, int width, int height, string name)
        {
            Guid guid;

            if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out guid))
            {
                string error = null;
                Uri url = new Uri(texture);
                string downloadedFileName = ReadTexture(texture, out error);
                if (downloadedFileName == null)
                {
                    return "@FAILURE: Could not read texture: " + error;
                }

                int numFrames = 1;
                List<string> textureFiles = new List<string>();

                try {

                    using (Bitmap bitmap = (Bitmap) Image.FromFile(downloadedFileName))
                    {
                        for (int row = 0; row < height; ++row) {
                            for (int col = 0; col < width; ++col) {
                                Size tileSize = bitmap.Size;
                                tileSize.Width = tileSize.Width / width;
                                tileSize.Height = tileSize.Height / height;
                                Bitmap newBitmap = RescaleImage(bitmap, GetBestSize(tileSize), row, col, height, width, out numFrames);
                                string newTextureFile = TempFile(".png");
                                newBitmap.Save(newTextureFile);
                                textureFiles.Add(newTextureFile);
                            }
                        }
                       
                    }

                    System.IO.File.Delete(downloadedFileName);
                }
                catch (Exception)
                {
                    return ("@FAILURE: The image file appears to be invalid.");
                }

                List<int> damages = new List<int>();
                error = null;

                UpdateZip(guid, (ZipFile zipFile) => {
                    int texNum = 0;

                    Dictionary<int, NameAndLore> namesAndLore = ReadNameAndLore(zipFile);

                    foreach (string textureFile in textureFiles) {
                        int damage = FirstFreeDamage(zipFile);
                        if (damage < 0)
                        {
                            error = "Maximum number of blocks reached";
                            return UpdateType.None;
                        }
                        damages.Add(damage);

                        string textureName = TextureName(damage);
                        zipFile.AddEntry(textureName,
                                         (entryName) => { return new FileStream(textureFile, FileMode.Open, FileAccess.Read); },
                                         (entryName, stream) => { stream.Close(); });

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

                        
                        namesAndLore[damage] = GetNameAndLore(damage, texture, name, texNum, width, height);

                        ++texNum;
                    }

                    WriteNameAndLore(zipFile, namesAndLore.Values);
                    return UpdateType.Primary;
                });

                foreach (string textureFile in textureFiles) {
                    System.IO.File.Delete(textureFile);
                }

                if (string.IsNullOrEmpty(error))
                {
                    return IntListToString(damages);
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

        private NameAndLore GetNameAndLore(int damage, string texture, string name, int texNum, int width, int height)
        {
            if (name == null)
                name = Path.GetFileNameWithoutExtension(new Uri(texture).LocalPath);

            string lore = "";
            if (width > 1 || height > 1) {
                int row = texNum / width;
                int col = texNum % width;
                lore = GetTiledLore(width, height, row, col);
            }

            return new NameAndLore(damage, name, lore);
        }

        private string GetTiledLore(int width, int height, int row, int col)
        {
            StringBuilder builder = new StringBuilder();

            for (int r = 0; r < height; ++r) {
                for (int c = 0; c < width; ++c) {
                    if (r == row && c == col) {
                        builder.Append("\u25A0 ");
                    }
                    else {
                        builder.Append("\u25A1 ");
                    }
                }

                if (r != height-1)
                    builder.Append("\r\n");
            }

            return builder.ToString();
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

                    Dictionary<int, NameAndLore> namesAndLore = ReadNameAndLore(zipFile);
                    namesAndLore.Remove(damage);
                    WriteNameAndLore(zipFile, namesAndLore.Values);

                    return UpdateType.Primary;
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

        Bitmap RescaleImage(Bitmap source, Size destSize, int row, int col, int totalRows, int totalCols, out int numFrames)
        {
            Size sourceSize = source.Size;
            numFrames = 1;
            try {
                numFrames = source.GetFrameCount(FrameDimension.Time);
            }
            catch (Exception) { }

            if (numFrames > 1)
            {
                var bmp = new Bitmap(destSize.Width, destSize.Height * numFrames, PixelFormat.Format32bppArgb);

                using (var gr = Graphics.FromImage(bmp)) {
                    gr.Clear(Color.Transparent);
                    gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    for (int frame = 0; frame < numFrames; ++frame) {
                        source.SelectActiveFrame(FrameDimension.Time, frame);

                        Rectangle dest = new Rectangle(0, destSize.Height * frame, destSize.Width, destSize.Height);
                        Rectangle src = Rectangle.FromLTRB(col * sourceSize.Width / totalCols, row * sourceSize.Height / totalRows, (col + 1) * sourceSize.Width / totalCols, (row + 1) * sourceSize.Height / totalRows);

                        gr.DrawImage(source, dest, src, GraphicsUnit.Pixel);
                    }
                }

                return bmp;
            }
            
            if (source.Height >= source.Width * 2 && source.Height % source.Width == 0) {
                // Minecraft-style animated texture.

                numFrames = source.Height / source.Width;
                sourceSize.Height = source.Width;

                var bmp = new Bitmap(destSize.Width, destSize.Height * numFrames, PixelFormat.Format32bppArgb);

                using (var gr = Graphics.FromImage(bmp)) {
                    gr.Clear(Color.Transparent);
                    gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    for (int frame = 0; frame < numFrames; ++frame) {
                        int srcFrameOffset = sourceSize.Height * frame;
                        Rectangle dest = new Rectangle(0, destSize.Height * frame, destSize.Width, destSize.Height);
                        Rectangle src = Rectangle.FromLTRB(col * sourceSize.Width / totalCols, srcFrameOffset + row * sourceSize.Height / totalRows, 
                                                           (col + 1) * sourceSize.Width / totalCols, srcFrameOffset + (row + 1) * sourceSize.Height / totalRows);

                        if (src.Width * 2 <= dest.Width)
                            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        else
                            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                        gr.DrawImage(source, dest, src, GraphicsUnit.Pixel);
                    }
                }

                return bmp;
            }
            else 
            {
                var bmp = new Bitmap(destSize.Width, destSize.Height, PixelFormat.Format32bppArgb);

                using (var gr = Graphics.FromImage(bmp))
                {
                    gr.Clear(Color.Transparent);
                    gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    Rectangle dest = new Rectangle(0, 0, destSize.Width, destSize.Height);
                    Rectangle src = Rectangle.FromLTRB(col * sourceSize.Width / totalCols, row * sourceSize.Height / totalRows, (col + 1) * sourceSize.Width / totalCols, (row + 1) * sourceSize.Height / totalRows);

                    if (src.Width * 2 <= dest.Width)
                        gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    else
                        gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                    gr.DrawImage(source, dest, src, GraphicsUnit.Pixel);
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
            catch (Exception) {
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

        // Update the zip file to have the file given. It must contain the exact same contents. Returns
        // false if the entry already existed with the same contents, true if the entry was created or updated. 
        bool UpdateZipEntryWithFile(ZipFile zipFile, string entryName, string fileName)
        {
            byte[] newContents = System.IO.File.ReadAllBytes(Server.MapPath("~/App_Data/" + fileName));

            if (zipFile.ContainsEntry(entryName)) {
                ZipEntry entry = zipFile[entryName];
                MemoryStream memStream = new MemoryStream();
                entry.Extract(memStream);
                byte[] oldContents = memStream.ToArray();
                if (SameContents(oldContents, newContents)) {
                    return false;
                }
                else {
                    zipFile.RemoveEntry(entryName);
                }
            }

            zipFile.AddEntry(entryName, newContents);
            return true;
        }

        private bool SameContents(byte[] oldContents, byte[] newContents)
        {
            if (oldContents.Length != newContents.Length)
                return false;

            for (int i = 0; i < oldContents.Length; ++i) {
                if (oldContents[i] != newContents[i])
                    return false;
            }

            return true;
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

        Dictionary<int, NameAndLore> ReadNameAndLore(ZipFile zipFile)
        {
            Dictionary<int, NameAndLore> result = new Dictionary<int, NameAndLore>();

            if (zipFile.ContainsEntry("names.txt")) {
                using (Stream stream = zipFile["names.txt"].OpenReader()) {
                    using (TextReader reader = new StreamReader(stream, Encoding.UTF8)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            NameAndLore nameAndLore = new NameAndLore(line);
                            result[nameAndLore.Damage] = nameAndLore;
                        }
                    }
                }
            }

            return result;
        }

        void WriteNameAndLore(ZipFile zipFile, IEnumerable<NameAndLore> list)
        {
            if (zipFile.ContainsEntry("names.txt"))
                zipFile.RemoveEntry("names.txt");

            StringBuilder builder = new StringBuilder();
            foreach (NameAndLore nameAndLore in list) {
                builder.AppendLine(nameAndLore.ToLine());
            }

            zipFile.AddEntry("names.txt", builder.ToString(), Encoding.UTF8);
        }

        List<int> UsedDamages(Guid guid)
        {
            List<int> used = new List<int>();

            UpdateZip(guid, (ZipFile zipFile) =>
            {
                used = UsedDamages(zipFile);
                return UpdateType.None;
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

        string CustomBlockName()
        {
            return ModelsFolder() + "/custom_block.json";
        }

        string SmallCubeName()
        {
            return ModelsFolder() + "/small_cube.json";
        }

        string MobTransparentName()
        {
            return TexturesFolder() + "/mob_spawner.png";
        }

        string TexturesFolder()
        {
            return "assets/minecraft/textures/blocks";
        }

        string ModelsFolder()
        {
            return "assets/minecraft/models/block";
        }

        enum UpdateType { None, Primary, Secondary}


        CloudBlockBlob UpdateZip(Guid guid, Func<ZipFile, UpdateType> processZip)
        {
            string tempPath = TempFile(".zip");
            CloudBlockBlob blob = GetBlob(guid);
            blob.DownloadToFile(tempPath, FileMode.Create);
            ZipFile zipFile = ZipFile.Read(tempPath);
            UpdateType updateType = processZip(zipFile);
            zipFile.Save();
            zipFile.Dispose();
            if (updateType != UpdateType.None)
            {
                if (updateType == UpdateType.Secondary)
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



        class NameAndLore
        {
            public int Damage;
            public string Name;
            public string Lore;
            public NameAndLore(int damage, string name, string lore)
            {
                this.Damage = damage;
                this.Name = name;
                this.Lore = lore;
            }

            public NameAndLore(string text)
            {
                string[] fields = text.Split('&');
                if (fields.Length == 3) {
                    this.Damage = int.Parse(fields[0]);
                    this.Name = HttpUtility.UrlDecode(fields[1]);
                    this.Lore = HttpUtility.UrlDecode(fields[2]);
                }
            }

            public string ToLine()
            {
                return Damage.ToString() + "&" + HttpUtility.UrlEncode(Name ?? "") + "&" + HttpUtility.UrlEncode(Lore ?? "");
            }
        }
    }
}