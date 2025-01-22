using System.Text.RegularExpressions;

namespace SmmuIdHelper
{
    public static partial class Program
    {
        [GeneratedRegex(@"&[\w]*_smmu [\w\d]* [\w\d]*", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex GetDTBlockHandleCodeRegex();

        private const string LineColAlgoMarker = "AAAAAAAAAAAAAAAAAAAA";

        public static void Main(string[] args)
        {
            //string DTFolderLocation = @"C:\Users\Gus\Documents\Duo\DTS";
            string DTFolderLocation = @"C:\Users\Gus\Downloads\DuoDTS4..14";
            string WinNTSystemMMUDriverINFFileLocation = @"C:\Users\Gus\Documents\qcsmmu8180.inf";

            Console.WriteLine("Reading device tree files...");

            IEnumerable<string> dtsfiles = Directory.EnumerateFiles(DTFolderLocation, "*.dtsi", SearchOption.AllDirectories);

            IEnumerable<(string dts, string content)> DTSourceCodeFileContentMap = dtsfiles.Select(x => (x.Replace(DTFolderLocation, ""), File.ReadAllText(x)));

            Console.WriteLine("Reading NT Driver INF file...");

            string WinNTSystemMMUDriverINFFileContent = File.ReadAllText(WinNTSystemMMUDriverINFFileLocation);

            Console.WriteLine("Building SMMU Stream definitions from device tree files...");

            List<(string, string)> smmumappedelements = BuildSMMUMapOfStreamsFromDT(DTSourceCodeFileContentMap);

            Console.WriteLine("Comparing SMMU Stream definitions between NT Driver INF and Linux DTS...");

            (
                List<(string, string)> smmuStreamsFoundInBothNTDriverINFAndDTS,
                List<(string, string)> notMatchedSMMUStreamDefinitionsBetweenDTSAndINF
            ) = BuildListsOfMatchResultsBetweenNTINFAndLinuxDTS(smmumappedelements, WinNTSystemMMUDriverINFFileContent);

            Console.WriteLine("Genering Comments...");

            WinNTSystemMMUDriverINFFileContent = CommentOut(WinNTSystemMMUDriverINFFileContent, smmuStreamsFoundInBothNTDriverINFAndDTS, notMatchedSMMUStreamDefinitionsBetweenDTSAndINF);

            string commentedFileDestination = $"{WinNTSystemMMUDriverINFFileLocation}.commented";

            Console.WriteLine($"Writing commented INF file to disk at: {commentedFileDestination}");

            File.WriteAllText(commentedFileDestination, WinNTSystemMMUDriverINFFileContent);
        }

        public static List<(string, string)> BuildSMMUMapOfStreamsFromDT(
            IEnumerable<(string DTSourceCodeFileLocation, string DTSourceCodeFileContent)> DTSourceCodeFileContentMap
        )
        {
            Regex DTBlockHandleCodeRegex = GetDTBlockHandleCodeRegex();

            List<(string, string)> smmumappedelements = [];

            foreach ((string DTSourceCodeFileLocation, string DTSourceCodeFileContent) in DTSourceCodeFileContentMap)
            {
                MatchCollection DTBlockHandleCodeMatches = DTBlockHandleCodeRegex.Matches(DTSourceCodeFileContent);

                foreach (Match DTBlockHandleCodeMatch in DTBlockHandleCodeMatches)
                {
                    try
                    {
                        string matchValue = DTBlockHandleCodeMatch.Value;
                        string[] matchValueSplittedBySpace = matchValue.Split(' ');

                        // We do not need to look at destination in this version of the code
                        //string destinations = matchValueSplittedBySpace[0];
                        string streamIdString = matchValueSplittedBySpace[1];
                        string maskString = matchValueSplittedBySpace[2];

                        uint streamId = 0;
                        uint mask = 0;

                        if (streamIdString.StartsWith("0x"))
                        {
                            streamId = Convert.ToUInt32(streamIdString.Replace("0x", ""), 16);
                        }
                        else
                        {
                            streamId = Convert.ToUInt32(streamIdString);
                        }

                        if (maskString.StartsWith("0x"))
                        {
                            mask = Convert.ToUInt32(maskString.Replace("0x", ""), 16);
                        }
                        else
                        {
                            mask = Convert.ToUInt32(maskString);
                        }

                        string streamIdHex = streamId.ToString("X4");
                        string maskHex = mask.ToString("X4");

                        streamIdHex = $"0x{streamIdHex[..2]}, 0x{streamIdHex.Substring(2, 2)}";
                        maskHex = $"0x{maskHex[..2]}, 0x{maskHex.Substring(2, 2)}";

                        string SMMUStreamIDAndMaskForNTComment = $"; 0xFF, {streamIdHex}, {maskHex}";

                        // This is a weird hack, I know, do not worry about it.
                        List<string> lines = [.. DTSourceCodeFileContent.Insert(DTBlockHandleCodeMatch.Index, LineColAlgoMarker).Replace("\r\n", "\n").Split("\n")];

                        int lineNumber = lines.IndexOf(lines.First(x => x.Contains(LineColAlgoMarker)));
                        int lineColumn = lines.First(x => x.Contains(LineColAlgoMarker)).IndexOf(LineColAlgoMarker);

                        smmumappedelements.Add(
                        (
                            $"{DTSourceCodeFileLocation}:{lineNumber}:{lineColumn}", 
                            SMMUStreamIDAndMaskForNTComment
                        ));
                    }
                    catch { }
                }
            }

            return smmumappedelements;
        }

        private static (List<(string, string)>, List<(string, string)>) BuildListsOfMatchResultsBetweenNTINFAndLinuxDTS(
            List<(string, string)> smmumappedelements, 
            string WinNTSystemMMUDriverINFFileContent
        )
        {
            List<(string, string)> smmuStreamsFoundInBothNTDriverINFAndDTS = [];
            List<(string, string)> notMatchedSMMUStreamDefinitionsBetweenDTSAndINF = [];

            foreach ((string DTSourceCodeFileLocationWithLineAndColInformation, string SMMUStreamIDAndMaskForNTComment) in smmumappedelements)
            {
                Console.WriteLine(DTSourceCodeFileLocationWithLineAndColInformation);
                Console.WriteLine(SMMUStreamIDAndMaskForNTComment);

                ConsoleColor previousForegroundColor = Console.ForegroundColor;

                if (WinNTSystemMMUDriverINFFileContent.Contains(SMMUStreamIDAndMaskForNTComment, StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("ACK: Above SMMU Stream definition (ID + Mask) was discovered in both the NT Driver INF configuration and a device tree file!");

                    smmuStreamsFoundInBothNTDriverINFAndDTS.Add((DTSourceCodeFileLocationWithLineAndColInformation, SMMUStreamIDAndMaskForNTComment));
                }
                else if (WinNTSystemMMUDriverINFFileContent.Contains(SMMUStreamIDAndMaskForNTComment[..18], StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("MACK: Above SMMU Stream ID definition (ID ONLY) was discovered in both the NT Driver INF configuration and a device tree file BUT the Mask (MASK ONLY) is different in both configurations!");

                    notMatchedSMMUStreamDefinitionsBetweenDTSAndINF.Add((DTSourceCodeFileLocationWithLineAndColInformation, SMMUStreamIDAndMaskForNTComment));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("NACK: Above SMMU Stream definition (ID + Mask) was NOT discovered in both the NT Driver INF configuration and a device tree file!");

                    notMatchedSMMUStreamDefinitionsBetweenDTSAndINF.Add((DTSourceCodeFileLocationWithLineAndColInformation, SMMUStreamIDAndMaskForNTComment));
                }

                Console.ForegroundColor = previousForegroundColor;
                Console.WriteLine();
            }

            return (smmuStreamsFoundInBothNTDriverINFAndDTS, notMatchedSMMUStreamDefinitionsBetweenDTSAndINF);
        }

        private static string CommentOut(
            string WinNTSystemMMUDriverINFFileContent,

            List<(
                string DTSourceCodeFileLocationWithLineAndColInformation, 
                string SMMUStreamIDAndMaskForNTComment
                )> smmuStreamsFoundInBothNTDriverINFAndDTS,

            List<(
                string DTSourceCodeFileLocationWithLineAndColInformation, 
                string SMMUStreamIDAndMaskForNTComment
                )> notMatchedSMMUStreamDefinitionsBetweenDTSAndINF
        )
        {
            string[] lines = WinNTSystemMMUDriverINFFileContent.Replace("\r\n", "\n").Split("\n");

            string match1 = "                                ; 0xFF, ";

            foreach (string line in lines)
            {
                if (line.StartsWith(match1))
                {
                    string streamElement = line.Replace(match1, "; 0xFF, ")[..30];

                    bool perfectMatch = smmuStreamsFoundInBothNTDriverINFAndDTS.Any(x => x.SMMUStreamIDAndMaskForNTComment.Contains(streamElement, StringComparison.InvariantCultureIgnoreCase));

                    bool streamIDOnlyMatch = notMatchedSMMUStreamDefinitionsBetweenDTSAndINF.Any(x => x.SMMUStreamIDAndMaskForNTComment.Contains(streamElement[..18], StringComparison.InvariantCultureIgnoreCase));

                    if (!perfectMatch)
                    {
                        if (!streamIDOnlyMatch)
                        {
                            Console.WriteLine("INF element not found:");

                            string comment = " ;;;;;;;; Element not found in DT";

                            WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace($"{streamElement}{comment}", streamElement);

                            WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace(streamElement, $"{streamElement}{comment}");
                        }
                        else
                        {
                            Console.WriteLine("INF element found but with different mask:");

                            IEnumerable<string> elArr = notMatchedSMMUStreamDefinitionsBetweenDTSAndINF
                                .Where(x => x.SMMUStreamIDAndMaskForNTComment
                                                .Contains(streamElement[..18], StringComparison.InvariantCultureIgnoreCase))
                                .Select(e => e.DTSourceCodeFileLocationWithLineAndColInformation);

                            string el = string.Join(", ", elArr);

                            string comment = $" ;;;;;;;; Element found in DT with different mask: {el}";

                            WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace($"{streamElement}{comment}", streamElement);

                            WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace(streamElement, $"{streamElement}{comment}");
                        }

                        Console.WriteLine(streamElement);
                        Console.WriteLine();
                    }
                    else
                    {
                        IEnumerable<string> elArr = smmuStreamsFoundInBothNTDriverINFAndDTS
                            .Where(x => x.SMMUStreamIDAndMaskForNTComment
                                            .Contains(streamElement, StringComparison.InvariantCultureIgnoreCase))
                            .Select(e => e.DTSourceCodeFileLocationWithLineAndColInformation);

                        string el = string.Join(", ", elArr);

                        string comment = $" ;;;;;;;; Element found in DT: {el}";

                        if (streamIDOnlyMatch)
                        {
                            IEnumerable<string> elArr2 = notMatchedSMMUStreamDefinitionsBetweenDTSAndINF
                                .Where(x => x.SMMUStreamIDAndMaskForNTComment
                                                .Contains(streamElement[..18], StringComparison.InvariantCultureIgnoreCase))
                                .Select(e => e.DTSourceCodeFileLocationWithLineAndColInformation);

                            el = string.Join(", ", elArr2);

                            comment += $" ;;;;;;;; Element found in DT with different mask: {el}";
                        }

                        WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace($"{streamElement}{comment}", streamElement);
                        WinNTSystemMMUDriverINFFileContent = WinNTSystemMMUDriverINFFileContent.Replace(streamElement, $"{streamElement}{comment}");
                    }
                }
            }

            return WinNTSystemMMUDriverINFFileContent;
        }
    }
}