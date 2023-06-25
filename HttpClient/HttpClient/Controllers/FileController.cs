using Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace HttpClient.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase {

        private readonly DistributedFileSystemClient _client;
        public FileController(DistributedFileSystemClient client)
        {
            _client = client;
        }

        [HttpPost("CreateFile")]
        public async Task<ActionResult<string>> CreateFile(IFormFile file)
        {
            try
            {
                await _client.CreateFile(file);
                return Ok("File created");
            }
            catch (Exception e)
            {
                return StatusCode(500, "Something went wrong: " + e.Message);
            }
        }


        [HttpGet("ReadFile/{fileName}")]
        public async Task<IActionResult> ReadFile(string fileName)
        {
            try
            {
                var content = await _client.ReadFile(fileName);
                var byteArray = Encoding.ASCII.GetBytes(content);
                var stream = new MemoryStream(byteArray);
                return File(stream, "text/plain", $"{fileName}");
            }
            catch (Exception e)
            {
                return StatusCode(500, "Something went wrong: " + e.Message);
            }
        }

        [HttpDelete("DeleteFile")]
        public async Task<IActionResult> DeleteFile(string fileName)
        {
            try
            {
                await _client.DeleteFile(fileName);
                return Ok("File deleted");
            }
            catch (Exception e)
            {
                return StatusCode(500, "Something went wrong: " + e.Message);
            }
        }

        // PUT api/<FileController>/5
        [HttpGet("GetSize/{fileName}")]
        public async Task<ActionResult<string>> GetSize(string fileName)
        {
            try
            {
                var size = await _client.GetSize(fileName);
                return Ok($"File size: {size} bytes");
            }
            catch (Exception e)
            {
                return StatusCode(500, "Something went wrong: " + e.Message);
            }
        }
    }
}
