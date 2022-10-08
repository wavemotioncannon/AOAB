﻿using AOABO.Omnibus;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AOABO.Processor
{
    public class Processor
    {
        public List<CSS> CSS = new List<CSS>();
        public List<Chapter> Chapters = new List<Chapter>();
        public List<Image> Images = new List<Image>();
        public List<NavPoint> NavPoints = new List<NavPoint>();
        public List<string> Metadata = new List<string>();
        public string baseSortOrder;
        public string baseFolder;

        public void FullOutput(bool textOnly, string name = null)
        {
            var folder = $"{baseFolder}\\temp";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            if (name == null)
            {
                Console.WriteLine("Book Name");
                name = Console.ReadLine();
            }

            File.WriteAllText(folder + "\\css.css", CSS.Aggregate(string.Empty, (file, css) => string.Concat(file, $"{css.Name} {css.Contents}\r\n")));
            List<string> manifest = new List<string>();
            List<string> spine = new List<string>();
            manifest.Add($"    <item id={"\""}css{"\""} href={"\""}css.css{"\""} media-type={"\""}text/css{"\""}/>");

            if (!textOnly)
            {
                Directory.CreateDirectory(folder + "\\images");

                foreach (var im in Images.Where(x => x.Referenced))
                {
                    if (im.Name.Equals("cover.jpg"))
                    {
                        File.Copy(im.OldLocation, folder + "\\" + im.Name);
                        manifest.Add($"    <item id={"\""}im{Images.IndexOf(im)}{"\""} href={"\""}{im.Name}{"\""} media-type={"\""}image/jpeg{"\""}/>");
                    }
                    else
                    {
                        File.Copy(im.OldLocation, folder + "\\images\\" + im.Name);
                        manifest.Add($"    <item id={"\""}im{Images.IndexOf(im)}{"\""} href={"\""}images/{im.Name}{"\""} media-type={"\""}image/jpeg{"\""}/>");
                    }
                }
            }

            Directory.CreateDirectory(folder + "\\text");
            int tocCounter = 0;

            foreach (var chapter in Chapters.OrderBy(x => x.SubFolder + "\\" + x.SortOrder))
            {
                string subdir = folder;
                string cssLink = string.Empty;
                if (!string.IsNullOrWhiteSpace(chapter.SubFolder))
                {
                    subdir = $"{ folder }\\{ chapter.SubFolder}";
                    cssLink = chapter.SubFolder.Split('\\').Aggregate(string.Empty, (agg, str) => string.Concat(agg, "../"));
                    Directory.CreateDirectory(subdir);
                }

                while (File.Exists($"{subdir}\\{chapter.FileName}"))
                {
                    chapter.SortOrder = chapter.SortOrder + "x";
                }

                var subFolderSplit = chapter.SubFolder.Split('\\');
                List<NavPoint> nps = NavPoints;

                foreach (var fold in subFolderSplit)
                {
                    if (fold.Equals("text")) continue;
                    var index = fold.IndexOf('-');
                    string folderName;
                    if (index == -1)
                    {
                        folderName = fold;
                    }
                    else
                    {
                        folderName = fold.Substring(index + 1);
                    }

                    if (!string.IsNullOrWhiteSpace(chapter.SubFolder))
                    {
                        var np = nps.FirstOrDefault(x => x.Label.Equals(folderName));
                        if (np == null)
                        {
                            np = new NavPoint { Label = folderName, Source = Uri.EscapeUriString(chapter.SubFolder.Replace('\\', '/') + "/" + chapter.FileName), Id = tocCounter };
                            tocCounter++;
                            nps.Add(np);
                        }
                        nps = np.navPoints;
                    }
                }

                if (textOnly)
                {
                    chapter.Contents = ImgRemover.Replace(chapter.Contents, string.Empty);
                }
                else
                {
                    var imFolderReplace = subFolderSplit.Aggregate("images", (agg, str) => string.Concat("../", agg));
                    chapter.Contents = chapter.Contents.Replace("[ImageFolder]", imFolderReplace);
                }

                nps.Add(new NavPoint { Label = chapter.Name, Source = Uri.EscapeUriString(chapter.SubFolder.Replace('\\', '/') + "/" + chapter.FileName), Id = tocCounter });
                tocCounter++;


                File.WriteAllText($"{subdir}\\{chapter.FileName}", $@"<?xml version='1.0' encoding='utf-8'?>
<html xmlns={"\""}http://www.w3.org/1999/xhtml{"\""} xmlns:epub={"\""}http://www.idpf.org/2007/ops{"\""} xml:lang={"\""}en{"\""}>
  <head>
    <meta http-equiv={"\""}Content-Type{"\""} content={"\""}text/html; charset=utf-8{"\""} />
  <link rel={"\""}stylesheet{"\""} type={"\""}text/css{"\""} href={"\""}{cssLink}css.css{"\""} />
</head>{ chapter.Contents}</html>");
                manifest.Add($"    <item id={"\""}id{Chapters.IndexOf(chapter)}{"\""} href={"\""}{chapter.SubFolder.Replace('\\', '/')}/{chapter.FileName}{"\""} media-type={"\""}application/xhtml+xml{"\""}/>");
                spine.Add($"    <itemref idref={"\""}id{Chapters.IndexOf(chapter)}{"\""}/>");
            }

            manifest.Add("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");

            File.WriteAllText($"{folder}\\content.opf",
    $@"<?xml version={"\""}1.0{"\""} encoding={"\""}UTF-8{"\""}?>
<package xmlns={"\""}http://www.idpf.org/2007/opf{"\""} version={"\""}2.0{"\""} unique-identifier={"\""}uuid_id{"\""}>
  <metadata xmlns:opf={"\""}http://www.idpf.org/2007/opf{"\""} xmlns:dc={"\""}http://purl.org/dc/elements/1.1/{"\""} xmlns:dcterms={"\""}http://purl.org/dc/terms/{"\""} xmlns:xsi={"\""}http://www.w3.org/2001/XMLSchema-instance{"\""} xmlns:calibre={"\""}http://calibre.kovidgoyal.net/2009/metadata{"\""}>
{Metadata.Aggregate(string.Empty, (agg, str) => string.Concat(agg, str, "\r\n"))}
  </metadata>
  <manifest>
{manifest.Aggregate(string.Empty, (agg, str) => string.Concat(agg, str, "\r\n"))}  </manifest>
  <spine toc={"\""}ncx{"\""}>
{spine.Aggregate(string.Empty, (agg, str) => string.Concat(agg, str, "\r\n"))}  </spine>
  <guide>
    <reference type={"\""}cover{"\""} href={"\""}images/{Images.FirstOrDefault().Name}{"\""} title={"\""}Cover{"\""}/>    
  </guide>
</package>
");

            File.WriteAllText($"{folder}\\toc.ncx", $"<?xml version='1.0' encoding='utf-8'?>\r\n<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\" xml:lang=\"en\">\r\n  <head>\r\n    <meta name=\"dtb:depth\" content=\"{NavPoints.Max(x => x.MaxTabs) + 2}\" />\r\n  </head>\r\n  <docTitle>\r\n    <text>{name}</text>\r\n  </docTitle>\r\n  <navMap>\r\n"
                + NavPoints.Aggregate(string.Empty, (agg, np) => string.Concat(agg, np, "\r\n")) + "  </navMap>\r\n</ncx>");

            Directory.CreateDirectory($"{folder}\\META-INF");
            File.WriteAllText($"{folder}\\META-INF\\container.xml", "<?xml version=\"1.0\"?>\r\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\r\n   <rootfiles>\r\n      <rootfile full-path=\"content.opf\" media-type=\"application/oebps-package+xml\"/>\r\n   </rootfiles>\r\n</container>");

            if (File.Exists($"{baseFolder}\\{name}.epub")) File.Delete($"{baseFolder}\\{name}.epub");
            ZipFile.CreateFromDirectory(folder, $"{baseFolder}\\{name}.epub");
            Directory.Delete(folder, true);
        }

        static Regex ImgRemover = new Regex("<img.*?\\/>");

        public void UnpackFolder(string folder)
        {
            LoadCSS(folder);

            LoadImages(folder);

            LoadText(folder);
        }

        private void LoadCSS(string folder)
        {
            var cssFiles = Directory.GetFiles(folder, "*.css", SearchOption.AllDirectories);
            var classStartRegex = new Regex("[A-Z,a-z,.]");

            foreach (var f in cssFiles)
            {
                var text = File.ReadAllText(f);
                int? start = null;
                int? space = null;
                int? open = null;
                for (int counter = 0; counter < text.Length; counter++)
                {
                    if (start == null && classStartRegex.IsMatch(text.Substring(counter, 1)))
                    {
                        start = counter;
                    }

                    if (start != null && space == null && text[counter].Equals(' '))
                    {
                        space = counter - 1;
                    }

                    if (space != null && open == null && text[counter].Equals('{'))
                    {
                        open = counter;
                    }

                    if (open != null && text[counter].Equals('}'))
                    {
                        var contents = text.Substring(open.Value, counter - open.Value + 1);
                        var match = CSS.FirstOrDefault(x => x.Contents.Equals(contents));
                        if (match != null)
                        {
                            match.OldNames.Add($"{f}:{text.Substring(start.Value, space.Value - start.Value + 1)}");
                        }
                        else
                        {
                            var substring = text.Substring(start.Value, space.Value - start.Value + 1);

                            if (substring.StartsWith('.'))
                            {
                                var newCSS = new CSS
                                {
                                    Contents = contents,
                                    Name = $".Style{CSS.Count}",
                                    OldNames = new List<string>
                                    {
                                    $"{f}:{substring}"
                                    }
                                };

                                CSS.Add(newCSS);
                            }
                            else
                            {
                                var newCSS = new CSS
                                {
                                    Contents = contents,
                                    Name = substring,
                                    OldNames = new List<string>
                                    {
                                    $"{f}:{substring}"
                                    }
                                };

                                CSS.Add(newCSS);
                            }
                        }

                        start = null;
                        space = null;
                        open = null;
                    }
                }
            }
        }

        private void LoadImages(string folder)
        {
            var imageFiles = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories)
                .Union(Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                .Union(Directory.GetFiles(folder, "*.jpeg", SearchOption.AllDirectories))
                .ToList();

            foreach (var im in imageFiles)
            {
                var oldName = im.Split('\\').Last();
                var extension = oldName.Split('.').Last();
                Images.Add(new Image
                {
                    OldLocation = im,
                    Name = $"{Images.Count:0000}.{extension}"
                });
            }
        }

        private void LoadText(string folder)
        {
            var files = GetHtmlFiles(folder).OrderBy(x => x).ToList();

            foreach (var f in files)
            {
                ImportHtmlFile(folder, f);
            }
        }

        private static List<string> GetHtmlFiles(string folder)
        {
            var list = new List<string>();
            list.AddRange(Directory.GetFiles(folder, "*.html"));
            list.AddRange(Directory.GetFiles(folder, "*.xhtml"));

            foreach (var d in Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly))
            {
                list.AddRange(GetHtmlFiles(d));
            }

            return list;
        }

        static Regex cssLinks = new Regex("href=\".*css\"");
        static Regex bodyRegex = new Regex("<body[\\s\\S]*</body>");
        static Regex classRegex = new Regex("class=\".*?\"");
        static Regex imageRegex = new Regex("(src|xlink:href)=\".*?\"");

        private void ImportHtmlFile(string baseFolder, string file)
        {
            var chapter = new Chapter { CssFiles = new List<string>() };
            Chapters.Add(chapter);

            var text = File.ReadAllText(file);
            var folders = file.Replace(baseFolder + "\\", string.Empty).Split('\\').ToList();
            var last = folders.Last();
            var index = last.IndexOf('-');
            if (index == -1)
            {
                chapter.Name = last;
                chapter.SortOrder = "000";
            }
            else
            {
                chapter.Name = last.Substring(index + 1);
                chapter.SortOrder = last.Substring(0, index);
            }
            folders.Remove(folders.Last());
            chapter.SubFolder = folders.Count > 0 ? folders.Aggregate(string.Empty, (agg, dir) => string.Concat(agg, "\\", dir)).Substring(1) : string.Empty;

            foreach (var cssLinkObject in cssLinks.Matches(text))
            {
                var cssLink = (Match)cssLinkObject;
                var link = text.Substring(cssLink.Index, cssLink.Length).Replace("href=\"", string.Empty).Replace("\"", string.Empty).Replace(" rel=stylesheet type=text/css", string.Empty);
                var folderSplit = link.Replace(baseFolder, string.Empty).Split('/').ToList();

                var fo = new List<string>(folders);
                foreach (var dir in folderSplit)
                {
                    if (dir.Equals(".."))
                    {
                        fo.Remove(fo.Last());
                    }
                    else
                    {
                        fo.Add(dir);
                    }
                }
                chapter.CssFiles.Add(fo.Aggregate(baseFolder, (agg, dir) => string.Concat(agg, "\\", dir)));
            }

            if (chapter.CssFiles.Count == 0)
            {
                chapter.CssFiles.Add(baseFolder + "\\css.css");
            }

            chapter.Contents = bodyRegex.Match(text).Value;
            List<Tuple<string, string>> CssClassReplacements = new List<Tuple<string, string>>();

            foreach (Match match in classRegex.Matches(chapter.Contents))
            {
                var cssclassname = match.Value.Substring(match.Value.IndexOf(' ') + 1).Replace("class=\"", string.Empty).Replace("\"", string.Empty);

                var names = cssclassname.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var name in names)
                {
                    var css = CSS.FirstOrDefault(x => chapter.CssFiles.Select(y => y + ":." + name).Any(y => x.OldNames.Any(z => z.Equals(y))));
                    if (css == null)
                    {
                        css = CSS.FirstOrDefault(x => chapter.CssFiles.Select(y => y + ":" + name).Any(y => x.OldNames.Any(z => z.Equals(y))));
                    }

                    if (css != null)
                    {
                        CssClassReplacements.Add(new Tuple<string, string>(name, css.Name));
                    }
                }
            }

            foreach (Match match in classRegex.Matches(chapter.Contents))
            {
                var repl = match.Value;
                foreach (var rep in CssClassReplacements.GroupBy(x => x.Item2, x => x.Item1))
                {
                    foreach (var orig in rep.Distinct())
                    {
                        repl = repl.Replace(orig, rep.Key);
                    }
                }

                chapter.Contents = chapter.Contents.Replace(match.Value, repl);
            }

            List<Tuple<string, string>> ImageReplacements = new List<Tuple<string, string>>();
            foreach (var imMatch in imageRegex.Matches(chapter.Contents))
            {
                var match = (Match)imMatch;

                var imageFile = match.Value.Replace("src=\"", string.Empty).Replace("xlink:href=\"", string.Empty).Replace("\"", string.Empty);
                var loc = new List<string>(folders);
                foreach (var imFileBit in imageFile.Split('/'))
                {
                    if (imFileBit.Equals(".."))
                    {
                        loc.Remove(loc.Last());
                    }
                    else
                    {
                        loc.Add(imFileBit);
                    }
                }

                var imFileLocation = loc.Aggregate(baseFolder, (agg, str) => string.Concat(agg, "\\", str));
                var im = Images.FirstOrDefault(x => x.OldLocation.Equals(imFileLocation, StringComparison.OrdinalIgnoreCase));

                if (im == null)
                {
                    if (imageFile.StartsWith("[ImageFolder]"))
                    {
                        im = Images.FirstOrDefault(x => x.OldLocation.Equals(baseFolder + "\\images\\" + imageFile.Split('/').Last(), StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (im != null)
                {
                    im.Referenced = true;
                    ImageReplacements.Add(new Tuple<string, string>(imageFile, $"[ImageFolder]/{im.Name}"));
                }
            }

            foreach (var rep in ImageReplacements.Where(x => !x.Item1.Equals(x.Item2)).GroupBy(x => x.Item2, x => x.Item1))
            {
                foreach (var orig in rep.Distinct())
                {
                    chapter.Contents = chapter.Contents.Replace(orig, rep.Key);
                }
            }
        }
    }
}