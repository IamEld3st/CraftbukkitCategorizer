using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using YamlDotNet.Serialization;
using Newtonsoft.Json.Linq;
using ICSharpCode.SharpZipLib.Zip;
using YamlDotNet.RepresentationModel;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SevenZipExtractor;

namespace CraftbukkitCategorizer
{
    class Program
    {
        static void Main(string[] args)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string baseDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string rawPath = baseDir + @"\rawFiles";
            string tempPath = baseDir + @"\temp";
            string outPath = baseDir + @"\output";
            string[] processJar(string filePath)
            {
                Console.WriteLine("Extracting " + filePath);
                System.Diagnostics.Process SZip = new System.Diagnostics.Process();
                string zipArgs = "x " + filePath + " -o" + tempPath;
                Console.WriteLine(filePath);
                Console.WriteLine(tempPath);
                Console.WriteLine(zipArgs);
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                startInfo.FileName = baseDir + @"\tools\7zip\7z.exe";
                startInfo.Arguments = zipArgs;
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
                string[] output = new string[5];
                // 0 - type ("craftbukkit","original","other")
                // 1 - buildDate
                // 2 - buildNo
                // 3 - mcVersion
                // 4 - FileHash
                
                // Type Detection
                if (System.IO.Directory.Exists(tempPath + @"\org\bukkit"))
                {
                    if (System.IO.File.Exists(tempPath + @"\defaults\glowstone.yml"))
                    {
                        output[0] = "glowstone";
                    }
                    else if (System.IO.Directory.Exists(tempPath + @"\org\spigotmc"))
                    {
                        output[0] = "spigot";
                    }
                    else
                    {
                        output[0] = "craftbukkit";
                    }
                }
                else if (!System.IO.Directory.Exists(tempPath + @"\META-INF\maven")) output[0] = "original";
                else output[0] = "unknown";
                Console.WriteLine("Type: "+output[0]);

                // Build Number Detection
                string[] manifestFile = System.IO.File.ReadAllLines(tempPath+@"\META-INF\MANIFEST.MF");
                string[] tmpBN = new string[2];
                foreach (var line in manifestFile)
                {
                    if (line.Contains("Build-Version") || line.Contains("Implementation-Version"))
                    {
                        string[] lineSplit = line.Split('-');
                        string RawBuildNo = lineSplit[lineSplit.Length - 1];
                        char[] buildNoArray = RawBuildNo.ToCharArray();
                        for (int i = 0; i < buildNoArray.Length; i++)
                        {
                            if (Char.IsNumber(buildNoArray[i]))
                            {
                                tmpBN[0] += buildNoArray[i];
                            }
                        }
                    }
                    if (line.Contains("Built-By: "))
                    {
                        tmpBN[1] = line.Remove(0, 10).Replace(' ', '_');
                    }
                }
                output[2] = tmpBN[0]+tmpBN[1];
                Console.WriteLine("BuildNo: " + output[2]);

                // Build Date Detection
                DateTime mavenBuildDate = System.IO.Directory.GetLastWriteTimeUtc(tempPath + @"\META-INF\maven");
                output[1] = mavenBuildDate.ToString().Replace(" ", "").Replace(".", "").Replace(":", "");
                Console.WriteLine("BuildDate: "+output[1]);

                // MCVersion Detection
                if (output[0] == "craftbukkit")
                {
                    System.IO.File.Copy(tempPath + @"\net\minecraft\server\MinecraftServer.class", tempPath + @"\MinecraftServer.class", true);
                    System.Diagnostics.Process JAD = new System.Diagnostics.Process();
                    JAD.StartInfo.FileName = baseDir + @"\tools\jad.exe";
                    JAD.StartInfo.Arguments = tempPath + @"\MinecraftServer.class";
                    JAD.StartInfo.UseShellExecute = false;
                    JAD.StartInfo.RedirectStandardInput = true;
                    JAD.StartInfo.RedirectStandardOutput = true;
                    JAD.StartInfo.RedirectStandardError = true;
                    JAD.Start();
                    JAD.WaitForExit();

                    string[] decompiledClass = System.IO.File.ReadAllLines(tempPath + @"\MinecraftServer.jad");

                    for (int i = 0; i < decompiledClass.Length; i++)
                    {
                        if (decompiledClass[i].Contains("Starting minecraft server version"))
                        {
                            // ([0-9]\.[0-9]\.[0-9]_[0-9][0-9]|[0-9]\.[0-9]_[0-9][0-9]|[0-9]\.[0-9]\.[0-9]|[0-9]\.[0-9])|version (B)eta|Prerelease ([0-9])
                            MatchCollection matches = Regex.Matches(decompiledClass[i], @"([0-9]\.[0-9]\.[0-9]_[0-9][0-9]|[0-9]\.[0-9]_[0-9][0-9]|[0-9]\.[0-9]\.[0-9]|[0-9]\.[0-9])|version (B)eta|Prerelease ([0-9])");
                            foreach (Match match in matches)
                            {
                                if (match.ToString().StartsWith("version"))
                                {
                                    output[3] += "b";
                                }
                                char[] matchArray = match.ToString().ToCharArray();
                                if (Char.IsNumber(matchArray[0]))
                                {
                                    output[3] += match;
                                }
                                if (match.ToString().StartsWith("Prerelease"))
                                {
                                    output[3] += "-pre" + match.ToString().Remove(0, 11);
                                }
                            }
                            Console.WriteLine("mcVersion: " + output[3]);
                            i = decompiledClass.Length;
                        }
                    }
                    System.IO.File.Delete(tempPath + @"\MinecraftServer.jad");
                }
                else
                {
                    output[3] = "unknown";
                }

                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = md5.ComputeHash(stream);
                        output[4] = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
                Console.WriteLine("File Hash: "+output[4]);
                
                return output;

            }
            string[] getFilePaths()
            {
                int jarsFound = 0;
                List<string> paths = new List<string>();

                string jarsDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + @"\rawFiles";
                Console.WriteLine("Jars directory: "+jarsDir);

                foreach (string file in System.IO.Directory.GetFiles(jarsDir))
                {
                    if (file.EndsWith(".jar"))
                    {
                        paths.Add(file.Substring(jarsDir.Length));
                        jarsFound += 1;
                    }
                }

                Console.WriteLine("Found "+jarsFound+" jars");

                return paths.ToArray();
            }
            void prepareFile(string fileName)
            {
                string filePath = rawPath + fileName;
                string destFile = tempPath + fileName;
                System.IO.File.Copy(filePath, destFile, true);
            }
            void saveFile(string type, string bDate, string bNum, string mcVersion, string hash, string fileName)
            {
                string filePath = rawPath + fileName;
                string destPath = outPath + @"\" + type + @"\" + mcVersion;
                string destFile = destPath + @"\"+type+"-"+bDate+"-"+bNum+"-"+hash+".jar";
                
                if (!System.IO.Directory.Exists(destPath))
                {
                    System.IO.Directory.CreateDirectory(destPath);
                }

                System.IO.File.Copy(filePath, destFile, true);
                //System.IO.File.Delete(filePath);
            }
            void createFolders()
            {
                if (!System.IO.Directory.Exists(rawPath))
                {
                    System.IO.Directory.CreateDirectory(rawPath);
                }
                if (!System.IO.Directory.Exists(tempPath))
                {
                    System.IO.Directory.CreateDirectory(tempPath);
                }
                if (!System.IO.Directory.Exists(outPath))
                {
                    System.IO.Directory.CreateDirectory(outPath);
                }
            }
            void ClearTemp()
            {
                System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(tempPath);
                foreach (System.IO.FileInfo file in directory.GetFiles()) file.Delete();
                foreach (System.IO.DirectoryInfo subDirectory in directory.GetDirectories()) subDirectory.Delete(true);
            }
            createFolders();
            System.IO.Directory.SetCurrentDirectory(tempPath);
            string[] files = getFilePaths();
            for (int i = 0; i < files.Length; i++)
            {
                var fileWatch = System.Diagnostics.Stopwatch.StartNew();
                //prepareFile(files[i]);
                string[] data = processJar(rawPath + files[i]);
                saveFile(data[0], data[1],data[2], data[3], data[4], files[i]);
                ClearTemp();
                fileWatch.Stop();
                Console.WriteLine("This file took "+fileWatch.ElapsedMilliseconds+" ms");
            }
            watch.Stop();
            Console.WriteLine("Whole process took "+watch.Elapsed);
            Console.ReadLine();
        }
    }
}
