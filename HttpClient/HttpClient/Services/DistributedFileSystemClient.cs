using Flurl.Http;
using HttpClient.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Client {
    public class DistributedFileSystemClient {
        private readonly string masterServerUrl;
        private readonly int replicationLevel;

        public DistributedFileSystemClient(IConfiguration configuration)
        {
            this.masterServerUrl = configuration.GetValue<string>("MasterServerUrl");
            replicationLevel = configuration.GetValue<int>("ReplicationLevel");
        }

        public async Task<bool> CreateFile(IFormFile file)
        {
            if (file.Length == 0)
            {
                System.Console.WriteLine("File is empty");
                return false;
            }

            string fileName = file.FileName;
            string fileContent = await ReadContentAsync(file);

            var chunks = SplitIntoChunks(fileContent);

            //get the list of chunk servers
            var chunkServers = await $"{masterServerUrl}/api/ChunkServers".GetJsonAsync<ChunkServer[]>();

            //get file metadata
            var fileResponse = await $"{masterServerUrl}/api/Files/".PostJsonAsync(new { Name = fileName, Size = fileContent.Length, NumberOfChunks = chunks.Length });
            var fileData = await fileResponse.GetJsonAsync();

            // Distribute the chunks to the chunk servers
            List<int> successfullChunks = new List<int>();
            for( int r = 0; r < replicationLevel; r++)
            {
                //shuffle chunk servers order so the chunks are distributed randomly to ensure better fault tolerance
                RandomShuffle(chunkServers);
                int chunkServerPointer = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    //select chunk server in round robin fashion
                    var chunkServer = chunkServers[chunkServerPointer % chunkServers.Length];
                    chunkServerPointer++;

                    //save chunk information in the master
                    var chunkResponse = await $"{masterServerUrl}/api/chunks".PostJsonAsync(new { FileId = fileData.id, ChunkServerUrl = chunkServer.host, ChunkNumber = i });
                    var chunkData = await chunkResponse.GetJsonAsync();

                    //save chunk content in the chunk server
                    var result = await $"{chunkServer.host}/api/Chunk/storeChunk".PostJsonAsync(new { Data = chunks[i], Id = chunkData.id });
                    if (!result.ResponseMessage.IsSuccessStatusCode)
                    {
                        System.Console.WriteLine($"Failed to store chunk at chunk server {chunkServer.host}");
                    }
                    successfullChunks.Add(i);
                }
            }
            
            //check that every chunk was saved at least once
            if (successfullChunks.Distinct().Count() == chunks.Length)
                return true;
            return false;
        }

        public async Task<string> ReadFile(string filename)
        {
            // Get the list of chunk servers where the file is distributed
            var chunkDataList = await $"{masterServerUrl}/api/Chunks/GetChunks/{filename}".GetJsonAsync<ChunkData[]>();

            //with this I'll get for each chunk number the list of chunk servers where the chunk is.
            var groupedChunks = chunkDataList.GroupBy(c => c.ChunkNumber).OrderBy(g => g.Key);

            var fileResponse = await $"{masterServerUrl}/api/Files/GetByName/{filename}".GetAsync();

            if (fileResponse.StatusCode == (int)HttpStatusCode.NotFound)
                throw new Exception("File was not found");

            var file = await fileResponse.GetJsonAsync();
            long numberOfChunks = file.numberOfChunks;

            //check that the number of chunks expected is the number of chunks in the database
            if (numberOfChunks != groupedChunks.Count())
                throw new Exception("File was not saved correctly");

            StringBuilder fileData = new StringBuilder();

            foreach(var g in groupedChunks)
            {
                bool successfullRead = false;
                foreach(var chunkData in g)
                {
                    //for each chunk try to read it from the first chunk sever that responds successfully
                    try
                    {
                        var chunkResponse = await $"{chunkData.ChunkServerUrl}/api/Chunk/getChunk/{chunkData.Id}".GetAsync();
                        if (!chunkResponse.ResponseMessage.IsSuccessStatusCode)
                            continue;
                        successfullRead = true;
                        var chunk = await chunkResponse.ResponseMessage.Content.ReadAsStringAsync();
                        fileData.Append(chunk);
                        break;
                    }
                    catch(Exception e)
                    {
                        continue;
                    }
                }

                if (!successfullRead)
                    throw new Exception("The file was impossible to read");
            }

            return fileData.ToString();
        }

        public async Task DeleteFile(string filename)
        {
            // Request file metadata from the master server
            var chunkDataList = await $"{masterServerUrl}/api/Chunks/GetChunks/{filename}".GetJsonAsync<ChunkData[]>();

            await $"{masterServerUrl}/api/Files/{filename}".DeleteAsync();

            foreach (var chunkData in chunkDataList)
            {
                await $"{chunkData.ChunkServerUrl}/api/Chunk/deleteChunk/{chunkData.Id}".DeleteAsync();
            }
        }

        public async Task<long> GetSize(string filename)
        {
            try
            {
                var fileResponse = await $"{masterServerUrl}/api/Files/GetByName/{filename}".GetAsync();
                if (fileResponse.StatusCode == (int)HttpStatusCode.NotFound)
                    return 0;

                var file = await fileResponse.GetJsonAsync();
                return file.size;
            }
            catch (Exception e)
            {
                return 0;
            }
        }

        private string[] SplitIntoChunks(string fileContent)
        {
            // Split the file content into chunks of 1KB each
            // This is a simplified implementation and doesn't handle unicode characters properly
            int chunkSize = 1024; // 1KB
            int chunksCount = (fileContent.Length + chunkSize - 1) / chunkSize;
            string[] chunks = new string[chunksCount];

            for (int i = 0; i < chunksCount; i++)
            {
                int startIndex = i * chunkSize;
                int length = chunkSize;

                if (startIndex + length > fileContent.Length)
                {
                    length = fileContent.Length - startIndex;
                }

                chunks[i] = fileContent.Substring(startIndex, length);
            }

            return chunks;
        }

        private void RandomShuffle( ChunkServer[] chunkServers )
        {
            Random rnd = new Random((int)DateTime.Now.Ticks);
            for( int i = 0; i < chunkServers.Length; i++ )
            {
                int position = rnd.Next(i, chunkServers.Length);
                ChunkServer a = chunkServers[i];
                chunkServers[i] = chunkServers[position];
                chunkServers[position] = a;
            }
        }
        private async Task<string> ReadContentAsync(IFormFile formFile)
        {
            using (var reader = new StreamReader(formFile.OpenReadStream()))
            {
                var content = await reader.ReadToEndAsync();
                return content;
            }
        }
    }
}
