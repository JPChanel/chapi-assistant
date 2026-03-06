using System;
using System.IO;
using System.Text;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordTool;

public class Program 
{
    public static void Main(string[] args)
    {
        if (args.Length == 0) return;
        string path = args[0];
        if (!File.Exists(path)) { Console.WriteLine("File not found"); return; }
        
        using (WordprocessingDocument doc = WordprocessingDocument.Open(path, false))
        {
            var body = doc.MainDocumentPart.Document.Body;
            foreach (var p in body.Descendants<Paragraph>())
            {
                var text = p.InnerText.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                Console.WriteLine(text);
            }
        }
    }
}
