﻿using Core;
using Core.Downloads;
using Core.Processor;
using OBB.JSONCode;
using System.IO.Compression;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace OBB
{
    public static class Builder
    {
        private static bool AFileIsAvailable(Series series, LibraryResponse? library)
        {
            var inFolder = Settings.MiscSettings.InputFolder == null ? Environment.CurrentDirectory :
                Settings.MiscSettings.InputFolder.Length > 1 && Settings.MiscSettings.InputFolder[1].Equals(':') ? Settings.MiscSettings.InputFolder : Environment.CurrentDirectory + "\\" + Settings.MiscSettings.InputFolder;
            if (series.Volumes.Any(x => File.Exists(inFolder + "\\" + x.FileName)))
                return true;

            if (library != null)
            {
                if (library.books.Any(x => series.Volumes.Any(y => y.ApiSlug.Equals(x.volume.slug))))
                    return true;
            }

            return false;
        }
        private static readonly Regex chapterTitleRegex = new Regex("<h1>[\\s\\S]*?<\\/h1>");
        private static Login Login;
        public static async Task SeriesLoop()
        {
            Dictionary<int, Series> seriesList;
            using (var reader = new StreamReader("JSON\\Series.json"))
            {
                var deserializer = new DataContractJsonSerializer(typeof(Series[]));
                var list = ((Series[])deserializer.ReadObject(reader.BaseStream)).ToList();

                LibraryResponse? library = null;
                if (Settings.MiscSettings.DownloadBooks)
                {
                    using (var client = new HttpClient())
                    {
                        Login = await Login.FromFile(client);
                        Login = Login ?? await Login.FromConsole(client);

                        if (Login != null)
                        {
                            library = await Downloader.GetLibrary(client, Login.AccessToken);
                        }
                    }
                }

                var i = 0;
                seriesList = list.Where(x => AFileIsAvailable(x, library)).OrderBy(x => x.Name).ToDictionary(x => i++, x => x);
            }

            while (true)
            {
                Console.Clear();
                foreach (var series in seriesList)
                {
                    Console.WriteLine($"{series.Key} - {series.Value.Name}");
                }

                var choice = Console.ReadLine();

                if (!int.TryParse(choice, out var i))
                {
                    break;
                }

                var selection = seriesList.FirstOrDefault(x => x.Key == i).Value;

                if (selection == null)
                {
                    continue;
                }

                Console.Clear();

                var inFolder = Settings.MiscSettings.InputFolder == null ? Environment.CurrentDirectory :
                    Settings.MiscSettings.InputFolder.Length > 1 && Settings.MiscSettings.InputFolder[1].Equals(':') ? Settings.MiscSettings.InputFolder : Environment.CurrentDirectory + "\\" + Settings.MiscSettings.InputFolder;

                if (!Directory.Exists(inFolder)) Directory.CreateDirectory(inFolder);

                if (Settings.MiscSettings.DownloadBooks)
                {
                    using (var client = new HttpClient())
                    {
                        if (Login != null)
                        {
                            await Downloader.DoDownloads(client, Login.AccessToken, inFolder, selection.Volumes.Select(x => new Name { ApiSlug = x.ApiSlug, FileName = x.FileName }));
                        }
                    }
                }


                var outputFolder = Settings.MiscSettings.OutputFolder == null ? Environment.CurrentDirectory :
                    Settings.MiscSettings.OutputFolder.Length > 1 && Settings.MiscSettings.OutputFolder[1].Equals(':') ? Settings.MiscSettings.OutputFolder : Environment.CurrentDirectory + "\\" + Settings.MiscSettings.OutputFolder;

                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

                foreach (var vol in selection.Volumes)
                {
                    var file = $"{inFolder}\\{vol.FileName}";
                    var temp = $"{inFolder}\\inputtemp\\{vol.ApiSlug}";
                    if (!File.Exists(file)) continue;

                    try
                    {
                        if (Directory.Exists(temp)) Directory.Delete(temp, true);
                        ZipFile.ExtractToDirectory(file, temp);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.Message} while unzipping file {file}");
                    }

                    Console.WriteLine(vol.FileName);
                }

                var inProcessor = new Processor()
                {
                    DisableHyphenProcessing = selection.DisableHyphenProcessing
                };
                var outProcessor = new Processor();
                inProcessor.UnpackFolder($"{inFolder}\\inputtemp");
                outProcessor.UnpackFolder($"{inFolder}\\inputtemp");
                outProcessor.Chapters.Clear();

                var volumes = new List<Volume>();

                using (var reader = new StreamReader($"JSON\\{selection.InternalName}.json"))
                {
                    var deserializer = new DataContractJsonSerializer(typeof(Volume[]));
                    volumes = ((Volume[])deserializer.ReadObject(reader.BaseStream)).ToList();
                }

                bool coverPicked = false;

                foreach (var vol in selection.Volumes)
                {
                    var temp = $"{inFolder}\\inputtemp\\{vol.ApiSlug}";
                    if (!Directory.Exists(temp)) continue;

                    var volume = volumes.FirstOrDefault(x => string.Equals(x.InternalName, vol.ApiSlug));
                    if (volume == null) continue;

                    if (!coverPicked)
                    {
                        var cover = $"{temp}\\OEBPS\\Images\\Cover.jpg";
                        if (File.Exists("cover.jpg")) File.Delete("cover.jpg");
                        File.Copy(cover, "cover.jpg");
                        coverPicked = true;
                    }

                    var inChapters = inProcessor.Chapters.Where(x => x.SubFolder.Contains(volume.InternalName)).ToList();
                    var chapters = BuildChapterList(volume, x => true);

                    foreach (var chapter in chapters)
                    {

                        try
                        {
                            bool notFirst = false;
                            var newChapter = new Core.Processor.Chapter
                            {
                                Contents = string.Empty,
                                CssFiles = new List<string>(),
                                Name = chapter.ChapterName + ".xhtml",
                                SubFolder = chapter.SubFolder
                            };

                            if (chapter.OriginalFilenames.Any())
                            {

                                newChapter.SortOrder = chapter.SortOrder;

                                foreach (var chapterFile in chapter.OriginalFilenames)
                                {
                                    try
                                    {
                                        var entry = inChapters.First(x => x.Name.Equals(chapterFile, StringComparison.InvariantCultureIgnoreCase));
                                        newChapter.CssFiles.AddRange(entry.CssFiles);
                                        var fileContent = entry.Contents;

                                        if (notFirst)
                                        {
                                            fileContent = fileContent.Replace("<body class=\"nomargin center\">", string.Empty).Replace("<body>", string.Empty);
                                        }
                                        else
                                        {
                                            notFirst = true;
                                        }
                                        newChapter.Contents = string.Concat(newChapter.Contents, fileContent.Replace("</body>", string.Empty));

                                        entry.Processed = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"{ex.Message} while processing file {chapterFile}");
                                    }
                                }

                                if (chapter.Splits.Any())
                                {
                                    var dict = chapter.Splits.ToDictionary(x => newChapter.Contents.IndexOf(x.SplitLine), x => x);
                                    var keys = dict.Keys.OrderByDescending(x => x);
                                    int? previousIndex = null;
                                    var divRegex = new Regex("<div class=\".*?\">");
                                    var div = divRegex.Match(newChapter.Contents).Value;
                                    foreach(var key in keys)
                                    {
                                        var split = previousIndex.HasValue ? newChapter.Contents.Substring(key + dict[key].SplitLine.Length, previousIndex.Value - key - dict[key].SplitLine.Length) : newChapter.Contents.Substring(key + dict[key].SplitLine.Length);

                                        var splitChapter = new Core.Processor.Chapter
                                        {
                                            Contents = $"<body>{div}<h1>{dict[key].Name}</h1>{split}</div></body>",
                                            CssFiles = newChapter.CssFiles,
                                            Name = dict[key].Name + ".xhtml",
                                            SubFolder = newChapter.SubFolder + $"\\{newChapter.SortOrder}-{newChapter.Name}",
                                            SortOrder = dict[key].SortOrder,
                                        };
                                        outProcessor.Chapters.Add(splitChapter);
                                        previousIndex = key;
                                    }

                                    if (chapter.KeepFirstSplitSection)
                                    {
                                        newChapter.Contents = newChapter.Contents.Substring(0, previousIndex ?? newChapter.Contents.Length) + "</div></body>";
                                        outProcessor.Chapters.Add(newChapter);
                                    }
                                }
                                else
                                {
                                    outProcessor.Chapters.Add(newChapter);
                                    newChapter.Contents = string.Concat(newChapter.Contents, "</body>");
                                }
                                if (Settings.ChapterSettings.UpdateChapterTitles)
                                {
                                    var match = chapterTitleRegex.Match(newChapter.Contents);
                                    if (match.Success)
                                        newChapter.Contents = newChapter.Contents.Replace(match.Value, $"<h1>{newChapter.Name}</h1>");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing chapter {chapter.ChapterName} in book {vol.ApiSlug}");
                            Console.WriteLine(ex.ToString());
                        }
                    }

                    if (vol.ShowRemainingFiles)
                    {
                        Console.WriteLine($"Unprocessed Files in volume {vol.ApiSlug}");
                        foreach (var entry in inChapters.Where(x => !x.Processed))
                        {
                            Console.WriteLine($"\tUnprocessed chapter {entry.Name}");
                        }
                    }
                }

                outProcessor.Metadata.Add("<meta name=\"cover\" content=\"images/cover.jpg\" />");
                outProcessor.Images.Add(new Core.Processor.Image { Name = "cover.jpg", Referenced = true, OldLocation = "cover.jpg" });

                var coverContents = File.ReadAllText("Reference\\cover.txt");

                outProcessor.Chapters.Add(new Core.Processor.Chapter { Contents = coverContents, Name = "Cover.xhtml", SortOrder = "0000", SubFolder = "" });

                if (File.Exists($"{selection.Name}.epub")) File.Delete($"{selection.Name}.epub");

                outProcessor.Metadata.Add(@$"<dc:title>{selection.Name}</dc:title>");
                outProcessor.Metadata.Add($"<dc:creator id=\"creator01\">{selection.Author}</dc:creator>");
                outProcessor.Metadata.Add("<meta property=\"display-seq\" refines=\"#creator01\">1</meta>");
                outProcessor.Metadata.Add($"<meta property=\"file-as\" refines=\"#creator01\">{selection.AuthorSort}</meta>");
                outProcessor.Metadata.Add("<meta property=\"role\" refines=\"#creator01\" scheme=\"marc:relators\">aut</meta>");
                outProcessor.Metadata.Add("<dc:language>en</dc:language>");
                outProcessor.Metadata.Add("<dc:publisher>J-Novel Club</dc:publisher>");
                outProcessor.Metadata.Add("<dc:identifier id=\"pub-id\">1</dc:identifier>");

                await outProcessor.FullOutput(outputFolder, 
                    false, 
                    Settings.MiscSettings.UseHumanReadableFileNames, 
                    Settings.MiscSettings.RemoveTempFolder, 
                    selection.Name, 
                    Settings.ImageSettings.MaxImageWidth,
                    Settings.ImageSettings.MaxImageHeight,
                    Settings.ImageSettings.ImageQuality);

                if (Directory.Exists($"{inFolder}\\inputtemp")) Directory.Delete($"{inFolder}\\inputtemp", true);

                Console.WriteLine($"\"{selection.Name}\" creation complete. Press any key to continue.");
                Console.ReadKey();
            }

        }

        private static List<JSONCode.Chapter> BuildChapterList(Volume volume, Func<JSONCode.Chapter, bool> filter)
        {
            var ret = new List<JSONCode.Chapter>();

            foreach(var chapter in volume.Chapters)
            {
                ret.AddRange(ProcessChapter(chapter, filter));
            }

            if (Settings.ChapterSettings.IncludeBonusChapters)
            {
                foreach (var chapter in volume.BonusChapters)
                {
                    ret.AddRange(ProcessChapter(chapter, filter));
                }
            }

            if (Settings.ChapterSettings.IncludeExtraContent)
            {
                foreach (var chapter in volume.ExtraContent)
                {
                    ret.AddRange(ProcessChapter(chapter, filter));
                }
            }

            if (volume.Gallery != null)
            {
                if (Settings.ImageSettings.IncludeBonusArtAtStartOfVolume || Settings.ImageSettings.IncludeInsertsAtStartOfVolume)
                {
                    ret.Add(volume.Gallery.SetFiles(Settings.ImageSettings.IncludeBonusArtAtStartOfVolume, Settings.ImageSettings.IncludeInsertsAtStartOfVolume, true));
                }

                if (Settings.ImageSettings.IncludeBonusArtAtEndOfVolume || Settings.ImageSettings.IncludeInsertsAtEndOfVolume)
                {
                    ret.Add(volume.Gallery.SetFiles(Settings.ImageSettings.IncludeBonusArtAtEndOfVolume, Settings.ImageSettings.IncludeInsertsAtEndOfVolume, false));
                }
            }

            return ret;
        }

        private static IEnumerable<JSONCode.Chapter> ProcessChapter(JSONCode.Chapter chapter, Func<JSONCode.Chapter, bool> filter)
        {
            if (!filter(chapter)) yield break;

            if (!Settings.ImageSettings.IncludeInsertsInChapters) chapter.RemoveInserts();

            yield return chapter;

            foreach (var c in chapter.Chapters)
            {
                c.SubFolder = string.IsNullOrWhiteSpace(chapter.SubFolder) ? $"{chapter.SortOrder}-{chapter.ChapterName}" : $"{chapter.SubFolder}\\{chapter.SortOrder}-{chapter.ChapterName}";

                foreach (var child in ProcessChapter(c, filter))
                {
                    yield return child;
                }
            }
        }
    }
}
