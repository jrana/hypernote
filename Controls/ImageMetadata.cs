using System;

namespace HyperNote.Controls
{
    public class ImageMetadata
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Format { get; set; } = "Unknown";
        public int Width { get; set; }
        public int Height { get; set; }
        public int ColorDepth { get; set; }
        public bool HasExif { get; set; }
        public string? CameraManufacturer { get; set; }
        public string? CameraModel { get; set; }
        public DateTime? DateTaken { get; set; }
        public string? ExposureTime { get; set; }
        public double? Aperture { get; set; }
        public int? IsoSpeed { get; set; }
        public double? FocalLength { get; set; }
        public string? Software { get; set; }
    }
}
