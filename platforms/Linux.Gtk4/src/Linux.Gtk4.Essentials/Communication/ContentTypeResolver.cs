using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication
{
    internal class ContentTypeResolver
    {
        static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        };

        public static string ResolveContentType(EmailAttachment attachment)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(attachment.ContentType))
                    return attachment.ContentType;               // caller set it explicitly
            }
            catch (NotImplementedException) { }                  // reference-assembly stub on Linux

            var ext = Path.GetExtension(attachment.FullPath);
            return Mime.TryGetValue(ext, out var type) ? type : "application/octet-stream";
        }
    }
}
