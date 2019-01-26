﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.IO;
using System.Diagnostics;
using System.Net;
using HtmlAgilityPack;

namespace YoutubePlaylistAlbumDownload
{
    
    class Program
    {
        #region Constants
        private static readonly string[] ValidExtensions = new string[]
        {
            ".m4a",
            ".M4A",
            ".mp3",
            ".MP3"
        };

        private static readonly string[] BinaryFiles = new string[]
        {
            "AtomicParsley.exe",
            "ffmpeg.exe",
            "ffprobe.exe",
            "youtube-dl.exe"
        };

        private const string youtubeDL = "youtube-dl.exe";

        private const string BinaryFolder = "bin";

        private const string DownloadInfoXml = "DownloadInfo.xml";

        private static List<DownloadInfo> DownloadInfos = new List<DownloadInfo>();

        private const string Logfile = "logfile.log";

        private const string DefaultCommandLine = "-i --playlist-reverse --youtube-skip-dash-manifest {0} {1} --match-filter \"{2}\" -o \"%(autonumber)s-%(title)s.%(ext)s\" --format m4a --embed-thumbnail {3}";

        private const string DateAfterCommandLine = "--dateafter";

        private const string YoutubeSongDurationCommandLine = "duration < 600";

        private const string YoutubeMixDurationCommandLine = "duration > 600";
        #endregion

        //also using initializers as defaults
        #region XML parsed Settings
        //if prompts should be used from command line entry
        public static bool NoPrompts = false;
        //if it should run the scripts
        public static bool RunScripts = false;
        //try to perform the HTML parsing
        public static bool HtmlParse = false;
        //if we should parse tags
        public static bool ParseTags = false;
        //if we should copy files
        public static bool CopyFiles = false;
        //if we shuld copy binary files
        public static bool CopyBinaries = true;
        //if we should delete binary files
        public static bool DeleteBinaries = true;
        //if we should update youtubedl
        public static bool UpdateYoutubeDL = true;
        //if we are saving the new date for the last time the script was run
        public static bool SaveNewDate = true;
        #endregion

        
        static void Main(string[] args)
        {
            //WriteToLog("Press enter to start");
            //https://stackoverflow.com/questions/11512821/how-to-stop-c-sharp-console-applications-from-closing-automatically
            //Console.ReadLine();

            //init tag parsing, load xml data
            //check to make sure download info xml file is present
            if (!File.Exists(DownloadInfoXml))
            {
                WriteToLog(string.Format("{0} is missing, application cannot continue", DownloadInfoXml));
                Console.ReadLine();
                return;
            }
            WriteToLog("Loading XML document");
            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(DownloadInfoXml);
            }
            catch (XmlException ex)
            {
                WriteToLog(ex.ToString());
                Console.ReadLine();
                return;
            }
            try
            {
                //https://www.freeformatter.com/xpath-tester.html#ad-output
                //get some default settings
                NoPrompts = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/NoPrompts").InnerText.Trim());
                UpdateYoutubeDL = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/UpdateYoutubeDL").InnerText.Trim());
                CopyBinaries = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/CopyBinaries").InnerText.Trim());
                HtmlParse = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/HtmlParse").InnerText.Trim());
                RunScripts = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/RunScripts").InnerText.Trim());
                SaveNewDate = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/SaveNewDate").InnerText.Trim());
                ParseTags = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/ParseTags").InnerText.Trim());
                CopyFiles = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/CopyFiles").InnerText.Trim());
                DeleteBinaries = bool.Parse(doc.SelectSingleNode("//DownloadInfo.xml/Settings/DeleteBinaries").InnerText.Trim());
                //for each xml element "DownloadInfo" in element "DownloadInfo.xml"
                foreach (XmlNode infosNode in doc.SelectNodes(string.Format("//{0}/{1}", DownloadInfoXml, nameof(DownloadInfo))))
                {
                    //this works for inner elements
                    //string test = infosNode.SelectSingleNode("//Folder").InnerText;
                    DownloadInfo temp = new DownloadInfo
                    {
                        //Folder = 
                        Folder = infosNode.Attributes[nameof(DownloadInfo.Folder)].Value.Trim(),
                        Album = infosNode.Attributes[nameof(DownloadInfo.Album)].Value.Trim(),
                        AlbumArtist = infosNode.Attributes[nameof(DownloadInfo.AlbumArtist)].Value.Trim(),
                        Genre = infosNode.Attributes[nameof(DownloadInfo.Genre)].Value.Trim(),
                        LastTrackNumber = int.Parse(infosNode.Attributes[nameof(DownloadInfo.LastTrackNumber)].Value.Trim()),
                        DownloadType = (DownloadType)Enum.Parse(typeof(DownloadType), infosNode.Attributes[nameof(DownloadInfo.DownloadType)].Value.Trim()),
                        LastDate = infosNode.Attributes[nameof(DownloadInfo.LastDate)].Value.Trim(),
                        DownloadURL = infosNode.Attributes[nameof(DownloadInfo.DownloadURL)].Value.Trim(),
                        FirstRun = bool.Parse(infosNode.Attributes[nameof(DownloadInfo.FirstRun)].Value.Trim()),
                    };
                    XmlNodeList pathsList = infosNode.ChildNodes;
                    //i can do it without lists
                    if(pathsList.Count > 0)
                    {
                        temp.CopyPaths = new string[pathsList.Count];
                        int i = 0;
                        foreach (XmlNode paths in pathsList)
                        {
                            //check to make sure the path is valid before trying to use later
                            if(!Directory.Exists(paths.InnerText))
                            {
                                WriteToLog("ERROR: the path");
                                WriteToLog(paths.Value);
                                WriteToLog("Does not exist!");
                                Console.ReadLine();
                                return;
                            }
                            temp.CopyPaths[i++] = paths.InnerText;
                        }
                    }
                    else
                    {
                        WriteToLog("ERROR: paths count is 0! for downloadFolder of folder attribute " + infosNode.Attributes[nameof(DownloadInfo.Folder)].Value);
                        return;
                    }
                    //and finally add it to the list
                    DownloadInfos.Add(temp);
                }
            }
            catch (Exception ex)
            {
                WriteToLog(ex.ToString());
                Console.ReadLine();
                return;
            }

            //run an update on youtube-dl
            //https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process?redirectedfrom=MSDN&view=netframework-4.7.2
            if (!NoPrompts)
            {
                UpdateYoutubeDL = GetUserResponse("UpdateYoutubeDL?");
            }
            if (UpdateYoutubeDL)
            {
                WriteToLog("Update YoutubeDL");
                if (!Directory.Exists(BinaryFolder))
                {
                    WriteToLog("ERROR: \"bin\" folder missing");
                    Console.ReadLine();
                    return;
                }
                if(!File.Exists(Path.Combine(BinaryFolder,youtubeDL)))
                {
                    WriteToLog(string.Format("ERROR: {0} is missing in the {1} folder", youtubeDL, BinaryFolder));
                }
                try
                {
                    using (Process updateYoutubeDL = new Process())
                    {
                        //set properties
                        updateYoutubeDL.StartInfo.RedirectStandardError = false;
                        updateYoutubeDL.StartInfo.RedirectStandardOutput = false;
                        updateYoutubeDL.StartInfo.UseShellExecute = true;
                        updateYoutubeDL.StartInfo.WorkingDirectory = BinaryFolder;
                        updateYoutubeDL.StartInfo.FileName = youtubeDL;
                        updateYoutubeDL.StartInfo.CreateNoWindow = false;
                        updateYoutubeDL.StartInfo.Arguments = "--update";
                        updateYoutubeDL.Start();
                        updateYoutubeDL.WaitForExit();
                        if (updateYoutubeDL.ExitCode != 0)
                        {
                            WriteToLog(string.Format("ERROR: update process exited with code {0}", updateYoutubeDL.ExitCode));
                            Console.ReadLine();
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    WriteToLog(e.ToString());
                    Console.ReadLine();
                    return;
                }
            }
            else
                WriteToLog("UpdateYoutubeDl skipped");

            if (!NoPrompts)
            {
                CopyBinaries = GetUserResponse("CopyBinaries?");
            }
            if (CopyBinaries)
            {
                WriteToLog("Copy Binaries");
                if (!Directory.Exists(BinaryFolder))
                {
                    WriteToLog("ERROR: \"bin\" folder missing");
                    Console.ReadLine();
                    return;
                }
                foreach (string s in BinaryFiles)
                {
                    if (!File.Exists(Path.Combine(BinaryFolder, s)))
                    {
                        WriteToLog(string.Format("ERROR: file {0} is missing", s));
                        WriteToLog("Please download and place into \"bin\" folder");
                        Console.ReadLine();
                        return;
                    }
                }
                //copy the binaries to each folder
                foreach (DownloadInfo info in DownloadInfos)
                {
                    if (!Directory.Exists(info.Folder))
                    {
                        WriteToLog("ERROR: folder " + info.Folder + "does not exist!");
                        Console.ReadLine();
                        return;
                    }
                    if (info.DownloadType == DownloadType.Other1)
                    {
                        WriteToLog(string.Format("skipping folder {0} (downloadType = other1)", info.Folder));
                        continue;
                    }
                    foreach (string binaryFile in BinaryFiles)
                    {
                        WriteToLog(string.Format("Copying file {0} into folder {1}", binaryFile, info.Folder));
                        string fileToCopy = Path.Combine(info.Folder, binaryFile);
                        if (File.Exists(fileToCopy))
                            File.Delete(fileToCopy);
                        File.Copy(Path.Combine(BinaryFolder, binaryFile), fileToCopy);
                    }
                }
            }
            else
                WriteToLog("CopyBinaries skipped");
            
            //html parsing (testing...)
            if(!NoPrompts)
            {
                HtmlParse = GetUserResponse("HtmlParse?");
            }
            if(HtmlParse)
            {
                WriteToLog("Parsing HTML");
                foreach(DownloadInfo info in DownloadInfos)
                {
                    if(info.DownloadType != DownloadType.Other1)
                    {
                        WriteToLog(string.Format("skipping folder {0} (downloadType != other1)", info.Folder));
                        continue;
                    }
                    //delete any previous entries
                    foreach(string file in Directory.GetFiles(info.Folder,"*",SearchOption.TopDirectoryOnly))
                    {
                        if (ValidExtensions.Contains(Path.GetExtension(file)))
                            File.Delete(file);
                    }
                    WriteToLog("TODO: HTML PARSE");
                    continue;
                    using (WebClient client = new WebClient())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(client.DownloadString("https://hearthis.at/djflybeat/"));
                        //"/html[1]/body[1]/div[4]/div[1]/div[1]/section[1]/section[2]/div[1]/div[2]/div[1]/div[1]/ul[1]"
                        //https://stackoverflow.com/questions/15826875/html-agility-pack-using-xpath-to-get-a-single-node-object-reference-not-set
                        HtmlNode node = document.DocumentNode.SelectSingleNode("/html[1]/body[1]/div[4]/div[1]/div[1]/section[1]/section[2]/div[1]/div[2]/div[1]/div[1]/ul[1]");
                        List<HtmlNode> musicEntries = node.ChildNodes.Skip(1).ToList();
                        //.Where(element => element.properties
                        musicEntries = musicEntries.Where(entry => entry.Name.Equals("li")).ToList();
                        foreach (HtmlNode musicEntry in musicEntries)
                        {
                            HtmlNode article = musicEntry.ChildNodes[1];
                            HtmlNode span = article.ChildNodes[1];
                            HtmlNode entity = span.ChildNodes[0];
                            string page = entity.Attributes["href"].Value;
                            HtmlDocument songPage = new HtmlDocument();
                            songPage.LoadHtml(client.DownloadString(page));
                        }
                    }
                }
            }
            else
                WriteToLog("HtmlParse skipped");

            //ask user if we will run the scripts
            if (!NoPrompts)
            {
                RunScripts = GetUserResponse("RunScripts?");
            }
            if (RunScripts)
            {
                WriteToLog("Running scripts");
                //make sure binaries exist first
                foreach (DownloadInfo info in DownloadInfos)
                {
                    if (!Directory.Exists(info.Folder))
                    {
                        WriteToLog("ERROR: folder " + info.Folder + "does not exist!");
                        Console.ReadLine();
                        return;
                    }
                    if (info.DownloadType == DownloadType.Other1)
                    {
                        WriteToLog(string.Format("skipping folder {0} (downloadType = other1)", info.Folder));
                        continue;
                    }
                    foreach (string binaryFile in BinaryFiles)
                    {
                        string fileToCopy = Path.Combine(info.Folder, binaryFile);
                        if (!File.Exists(fileToCopy))
                        {
                            WriteToLog(string.Format("ERROR: binary file {0} does not exist in folder {1}", binaryFile, info.Folder));
                            Console.ReadLine();
                            return;
                        }
                    }
                }
                //build and run the process list
                //build first
                List<Process> processes = new List<Process>();
                foreach (DownloadInfo info in DownloadInfos)
                {
                    if (info.DownloadType == DownloadType.Other1)
                    {
                        WriteToLog(string.Format("skipping folder {0} (downloadType = other1)", info.Folder));
                        continue;
                    }
                    //delete any previous entries
                    foreach (string file in Directory.GetFiles(info.Folder, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (ValidExtensions.Contains(Path.GetExtension(file)))
                        {
                            WriteToLog("Deleting old file from previous run: " + Path.GetFileName(file));
                            File.Delete(file);
                        }
                    }
                    WriteToLog(string.Format("build process info folder {0}", info.Folder));
                    processes.Add(new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            RedirectStandardError = false,
                            RedirectStandardOutput = false,
                            UseShellExecute = true,
                            WorkingDirectory = info.Folder,
                            FileName = youtubeDL,
                            CreateNoWindow = false,
                            Arguments = string.Format(DefaultCommandLine,
                                info.FirstRun ? string.Empty : DateAfterCommandLine,
                                info.FirstRun ? string.Empty : info.LastDate,
                                info.DownloadType == DownloadType.YoutubeMix ? YoutubeMixDurationCommandLine : YoutubeSongDurationCommandLine,
                                info.DownloadURL)
                        }
                    });
                }
                //run them all now
                foreach(Process p in processes)
                {
                    try
                    {
                        WriteToLog(string.Format("Launching process for folder {0} using arguments {1}", p.StartInfo.WorkingDirectory, p.StartInfo.Arguments));
                        p.Start();
                    }
                    catch(Exception ex)
                    {
                        WriteToLog("An error has occurred running a process, stopping all");
                        foreach(Process proc in processes)
                        {
                            try
                            {
                                proc.Kill();
                            }
                            catch
                            {
                                //WriteToLog("process " + proc.Id + "not stopped");
                            }
                        }
                        WriteToLog(ex.ToString());
                        Console.ReadLine();
                        return;
                    }
                }
                //iterate to wait for all to complete
                foreach (Process p in processes)
                {
                    p.WaitForExit();
                    WriteToLog(string.Format("Process of folder {0} has finished or previously finished", p.StartInfo.WorkingDirectory));
                    p.Dispose();
                }
                GC.Collect();
                WriteToLog("All processes completed");
            }
            else
                WriteToLog("RunScripts skipped");

            //save the new date for when the above scripts were last run
            if (!NoPrompts)
            {
                SaveNewDate = GetUserResponse("SaveNewDate?");
            }
            if (SaveNewDate)
            {
                WriteToLog("Saving new date");
                //https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings
                string newDate = string.Format("{0:yyyyMMdd}", DateTime.Now);
                for(int i = 0; i < DownloadInfos.Count; i++)
                {
                    WriteToLog(string.Format("changing old date from {0} to {1}", DownloadInfos[i].LastDate, newDate));
                    DownloadInfos[i].LastDate = newDate;
                }
            }
            else
                WriteToLog("SaveNewDate skipped");

            //start naming and tagging
            //get a list of files from the directory listed
            if (!NoPrompts)
            {
                ParseTags = GetUserResponse("ParseTags?");
            }
            if(ParseTags)
            {
                WriteToLog("Parsing tags");
                for (int j = 0; j < DownloadInfos.Count; j++)
                {
                    DownloadInfo info = DownloadInfos[j];
                    WriteToLog("");
                    WriteToLog("-----------------------Parsing directory " + info.Folder + "----------------------");
                    if (!Directory.Exists(info.Folder))
                    {
                        WriteToLog("Directory " + info.Folder + " does not exist");
                        Console.ReadLine();
                        continue;
                    }
                    //make and filter out the lists
                    List<string> files = Directory.GetFiles(info.Folder).Where(file => ValidExtensions.Contains(Path.GetExtension(file))).ToList();
                    //check to make sure there are valid audio files before proceding
                    if (files.Count == 0)
                    {
                        WriteToLog("no valid audio files in directory");
                        continue;
                    }

                    //step 0: check for if padding is needed
                    //get the number of track numbers for the first and last files
                    int firstEntryNumTrackNums = Path.GetFileName(files[0]).Split('-')[0].Length;
                    int maxEntryNumTrackNums = 0;
                    foreach (string s in files)
                    {
                        string filename = Path.GetFileName(s);
                        int currentEntryNumTrackNums = filename.Split('-')[0].Length;
                        if (currentEntryNumTrackNums > maxEntryNumTrackNums)
                            maxEntryNumTrackNums = currentEntryNumTrackNums;
                    }
                    WriteToLog(string.Format("first entry, track number padding = {0}\nmax entry, track number padding = {1}\n",
                        firstEntryNumTrackNums, maxEntryNumTrackNums));
                    if (firstEntryNumTrackNums != maxEntryNumTrackNums)
                    {
                        //inform and ask
                        WriteToLog("Not equal! Pad entries?");
                        bool continuePad = false;
                        while (true)
                        {
                            if (bool.TryParse(Console.ReadLine(), out bool res))
                            {
                                continuePad = res;
                                break;
                            }
                            else
                            {
                                WriteToLog("Response must be true or false");
                            }
                        }
                        if (continuePad)
                        {
                            //use the last entry as reference point for how many paddings to do
                            for (int i = 0; i < files.Count; i++)
                            {
                                string oldFileName = Path.GetFileName(files[i]);
                                int numToPadOut = maxEntryNumTrackNums - oldFileName.Split('-')[0].Length;
                                if (numToPadOut > 0)
                                {
                                    string newFileName = oldFileName.PadLeft(oldFileName.Length + numToPadOut, '0');
                                    WriteToLog(string.Format("{0}\nrenamed to\n{1}", oldFileName, newFileName));
                                    System.IO.File.Move(Path.Combine(info.Folder, oldFileName), Path.Combine(info.Folder, newFileName));
                                    files[i] = Path.Combine(info.Folder, newFileName);
                                }
                                else
                                {
                                    WriteToLog(string.Format("{0}\nnot renamed", oldFileName));
                                }
                            }
                            //and re-sort if afterwards
                            files.Sort();
                        }
                        else
                        {
                            WriteToLog("Exiting!");
                            Console.ReadLine();
                            return;
                        }
                    }

                    //step 1: parse the tag info
                    for (int i = 0; i < files.Count; i++)
                    {
                        string fileName = files[i];
                        WriteToLog("Parsing " + fileName);

                        TagLib.Tag tag = null;
                        TagLib.File file = null;
                        try
                        {
                            //https://stackoverflow.com/questions/40826094/how-do-i-use-taglib-sharp
                            file = TagLib.File.Create(fileName);
                            tag = file.Tag;
                        }
                        catch (Exception ex)
                        {
                            WriteToLog(ex.ToString());
                            Console.ReadLine();
                            return;
                        }

                        //assign tag infos
                        //album
                        tag.Album = info.Album;
                        //album artist
                        //https://stackoverflow.com/questions/17292142/taglib-sharp-not-editing-artist
                        if (!string.IsNullOrEmpty(info.AlbumArtist))
                        {
                            tag.AlbumArtists = null;
                            tag.AlbumArtists = new string[] { info.AlbumArtist };
                        }
                        //last saved number in the xml will be the last track number applied
                        //so up it first, then use it
                        tag.Track = (uint)++info.LastTrackNumber;
                        string fileNameToParse = Path.GetFileNameWithoutExtension(fileName);
                        //replace "–" with "-", as well as "?-" with "-"
                        fileNameToParse = fileNameToParse.Replace('–', '-').Replace("?-", "-");
                        string[] splitFileName = fileNameToParse.Split('-');
                        //parse from name
                        switch (info.DownloadType)
                        {
                            case DownloadType.Other1:
                                //0 = track (discard), 1 = title
                                tag.Performers = null;
                                tag.Performers = new string[] { "VA" };
                                //tag.Title = splitFileName[1];//these already have title parsed
                                WriteToLog("Song treated as heartAtThis mix");
                                break;
                            case DownloadType.YoutubeMix:
                                //0 = track (discard), 1 = title
                                tag.Performers = null;
                                tag.Performers = new string[] { "VA" };
                                //trim is as well, just in case
                                //and join the whole thing back together, in case the jackass publisher uses "-" in the title
                                //https://stackoverflow.com/questions/12961868/split-and-join-c-sharp-string
                                tag.Title = string.Join("-", splitFileName.Skip(1)).Trim();
                                WriteToLog("Song treated as youtube mix");
                                break;
                            case DownloadType.YoutubeSong:
                                //0 = track (discard), 1 = artist, 2 = title
                                //need at least 3 entries for this to work
                                if (splitFileName.Count() < 3)
                                {
                                    WriteToLog("ERROR: not enough split entries for parsing, please enter manually! (count is " + splitFileName.Count() + " )");
                                    WriteToLog("Original: " + Path.GetFileNameWithoutExtension(fileName));
                                    WriteToLog("Enter new:");
                                    while (true)
                                    {
                                        string newFileName = Console.ReadLine();
                                        if (newFileName.Split('-').Count() < 3)
                                        {
                                            WriteToLog(string.Format("'{0}' does not have enough delimiters (need at least 3 to split)", newFileName));
                                        }
                                        else
                                        {
                                            splitFileName = newFileName.Split('-');
                                            break;
                                        }
                                    }
                                }
                                tag.Performers = null;
                                tag.Performers = new string[] { splitFileName[1].Trim() };
                                //trim it as well, just in case
                                //include anything after it rather than just get the last one, in case there's more split characters
                                tag.Title = string.Join("-", splitFileName.Skip(2)).Trim();
                                WriteToLog("Song treated as youtube song");
                                break;
                            default:
                                WriteToLog("Invalid downloadtype: " + info.DownloadType.ToString());
                                continue;
                        }
                        //genre and year applied the same ways for all
                        tag.Genres = null;
                        tag.Genres = new string[] { info.Genre };
                        tag.Year = (uint)DateTime.Now.Year;
                        file.Save();
                    }

                    //step 2: parse the filenames from the tags
                    foreach (string fileName in files)
                    {
                        //load the file again
                        TagLib.File file = null;
                        try
                        {
                            //https://stackoverflow.com/questions/40826094/how-do-i-use-taglib-sharp
                            file = TagLib.File.Create(fileName);
                        }
                        catch (Exception ex)
                        {
                            WriteToLog(ex.ToString());
                            Console.ReadLine();
                            return;
                        }
                        //get the old name
                        string oldFileName = Path.GetFileNameWithoutExtension(fileName);
                        //prepare the new name
                        string newFileName = string.Empty;
                        //manual check to make sure track and title exist
                        if (file.Tag.Track == 0)
                        {
                            while (true)
                            {
                                WriteToLog("ERROR: Track property is missing, please input manually!");
                                if (uint.TryParse(Console.ReadLine(), out uint result))
                                {
                                    file.Tag.Track = result;
                                    file.Save();
                                }
                            }
                        }
                        if (string.IsNullOrWhiteSpace(file.Tag.Title))
                        {
                            WriteToLog("ERROR: Title property is missing, please input manually!");
                            file.Tag.Title = Console.ReadLine();
                            file.Save();
                        }
                        switch (info.DownloadType)
                        {
                            case DownloadType.Other1:
                                //using the pre-parsed title...
                                newFileName = string.Format("{0}-{1}", file.Tag.Track.ToString(), file.Tag.Title);
                                break;
                            case DownloadType.YoutubeMix:
                                newFileName = string.Format("{0}-{1}", file.Tag.Track.ToString(), file.Tag.Title);
                                break;
                            case DownloadType.YoutubeSong:
                                if (file.Tag.Performers == null || file.Tag.Performers.Count() == 0)
                                {
                                    WriteToLog("ERROR: Artist property is missing, please input manually!");
                                    file.Tag.Performers = null;
                                    file.Tag.Performers = new string[] { Console.ReadLine() };
                                    file.Save();
                                }
                                newFileName = string.Format("{0}-{1} - {2}", file.Tag.Track.ToString(), file.Tag.Performers[0], file.Tag.Title);
                                break;
                            default:
                                WriteToLog("Invalid downloadtype: " + info.DownloadType.ToString());
                                Console.ReadLine();
                                continue;
                        }
                        //check for padding
                        //set padding to highest number of tracknumbers
                        //(if tracks go from 1-148, make sure filename for 1 is 001)
                        int trackPaddingLength = newFileName.Split('-')[0].Length;
                        int maxTrackNumPaddingLength = info.LastTrackNumber.ToString().Length;
                        if (trackPaddingLength < maxTrackNumPaddingLength)
                        {
                            WriteToLog("Correcting for track padding");
                            int numToPad = maxTrackNumPaddingLength - trackPaddingLength;
                            newFileName = newFileName.PadLeft(newFileName.Length + numToPad, '0');
                        }
                        //save the complete folder path
                        string completeFolderPath = Path.GetDirectoryName(fileName);
                        string completeOldPath = Path.Combine(completeFolderPath, oldFileName + Path.GetExtension(fileName));
                        string completeNewPath = Path.Combine(completeFolderPath, newFileName + Path.GetExtension(fileName));
                        WriteToLog(string.Format("renaming {0}\nto {1}", oldFileName, newFileName));
                        File.Move(completeOldPath, completeNewPath);
                    }

                    //at the end of each folder, write the new value back to the xml file
                    string xpath = string.Format("//{0}/{1}[@Folder='{2}']", DownloadInfoXml, nameof(DownloadInfo), info.Folder);
                    XmlNode infoNode = doc.SelectSingleNode(xpath);
                    if (infoNode == null)
                    {
                        WriteToLog("failed to save node back folder=" + info.Folder);
                        continue;
                    }
                    infoNode.Attributes["LastTrackNumber"].Value = info.LastTrackNumber.ToString();
                    doc.Save(DownloadInfoXml);
                    WriteToLog("Saved LastTrackNumber for folder " + info.Folder);
                }
            }

            //copy newly parsed files to their directories
            if (!NoPrompts)
            {
                CopyFiles = GetUserResponse("CopyFiles?");
            }
            if(CopyFiles)
            {
                WriteToLog("Copy Files");
                for (int j = 0; j < DownloadInfos.Count; j++)
                {
                    DownloadInfo info = DownloadInfos[j];
                    //make and filter out the lists
                    List<string> files = Directory.GetFiles(info.Folder).Where(file => ValidExtensions.Contains(Path.GetExtension(file))).ToList();
                    WriteToLog("");
                    WriteToLog("-----------------------CopyFiles for directory " + info.Folder + "----------------------");
                    if (files.Count == 0)
                    {
                        WriteToLog("no files to copy");
                        continue;
                    }
                    bool breakout = false;
                    //using copy for all then delete because you can't move across drives
                    foreach (string copypath in info.CopyPaths)
                    {
                        WriteToLog("Copying files to directory " + copypath);
                        //get a list of files in the copy directory for checking later
                        List<string> destinationDuplicateCheck = Directory.GetFiles(copypath).Where(file => ValidExtensions.Contains(Path.GetExtension(file))).ToList();
                        foreach (string file in files)
                        {
                            //check for duplicates by spitting the names and checking if the title is the same (skipping the track name)
                            string nameOfCurrentFileCheck = string.Join("-",Path.GetFileName(file).Split('-').Skip(1));
                            foreach(string fileInDestToCheck in destinationDuplicateCheck)
                            {
                                string naveofPresentFileCheck = string.Join("-", Path.GetFileName(fileInDestToCheck).Split('-').Skip(1));
                                if(naveofPresentFileCheck.Equals(nameOfCurrentFileCheck))
                                {
                                    WriteToLog("ERROR: file with similar name exists in" + info.Folder);
                                    WriteToLog(naveofPresentFileCheck);
                                    WriteToLog("Skipping, you will need to correct for duplicate entry and try again");
                                    Console.ReadLine();
                                    breakout = true;
                                    break;
                                }
                            }
                            if (breakout)
                                break;
                        }
                        if (breakout)
                            break;
                        //getting here means that all files were checked above and no duplicates exist
                        foreach(string file in files)
                        {
                            WriteToLog(Path.GetFileName(file));
                            string newPath = Path.Combine(copypath, Path.GetFileName(file));
                            File.Copy(file, newPath);
                        }
                    }
                    if (breakout)
                        continue;
                    //now delete
                    WriteToLog("Deleting files in infos folder");
                    foreach (string file in files)
                    {
                        if(File.Exists(file))
                        {
                            File.Delete(file);
                        }
                        else
                        {
                            WriteToLog("ERROR: file");
                            WriteToLog(file);
                            WriteToLog("Does not exist!");
                            Console.ReadLine();
                            return;
                        }
                    }
                }  
            }

            //delete the binaries in each folder
            if (!NoPrompts)
            {
                DeleteBinaries = GetUserResponse("DeleteBinaries?");
            }
            if (DeleteBinaries)
            {
                WriteToLog("Delete Binaries");
                if (!Directory.Exists(BinaryFolder))
                {
                    WriteToLog("ERROR: \"bin\" folder missing");
                    Console.ReadLine();
                    return;
                }
                foreach (DownloadInfo info in DownloadInfos)
                {
                    if (!Directory.Exists(info.Folder))
                    {
                        WriteToLog("ERROR: folder " + info.Folder + "does not exist!");
                        Console.ReadLine();
                        return;
                    }
                    if (info.DownloadType == DownloadType.Other1)
                    {
                        WriteToLog(string.Format("skipping folder {0} (downloadType = other1)", info.Folder));
                        continue;
                    }
                    foreach (string binaryFile in BinaryFiles)
                    {
                        WriteToLog(string.Format("Deleting file {0} in folder {1} if exist", binaryFile, info.Folder));
                        string fileToCopy = Path.Combine(info.Folder, binaryFile);
                        if (File.Exists(fileToCopy))
                            File.Delete(fileToCopy);
                    }
                }
            }
            else
                WriteToLog("DeleteBinaries skipped");

            //and we're all set here
            WriteToLog("Done");
            Console.ReadLine();
        }

        #region Helper Methods

        private static void WriteToLog(string logMessage)
        {
            Console.WriteLine(logMessage);
            File.AppendAllText(Logfile, string.Format("{0}:   {1}{2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), logMessage ,Environment.NewLine));
        }

        private static bool GetUserResponse(string question)
        {
            //ask user the question
            WriteToLog(question);
            while (true)
            {
                if (bool.TryParse(Console.ReadLine(), out bool res))
                {
                    return res;
                }
                else
                {
                    WriteToLog("Response must be bool parseable (i.e. true or false)");
                }
            }
        }
        #endregion
    }
}
