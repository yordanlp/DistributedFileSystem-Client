using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HttpClient.Models {
    public class ChunkData {
        public int Id { get; set; }
        public int FileId { get; set; }
        public int ChunkNumber { get; set; }
        public string ChunkServerUrl { get; set; }
    }
}
