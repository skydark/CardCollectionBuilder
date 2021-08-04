namespace CardCollectionBuilder
{
    using CommandLine;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(Run);
        }

        class Options
        {
            [Value(0, Required = true, HelpText = "Collection definition dir")]
            public string InDir { get; set; }

            [Option("workdir", Default = "build/{collection}_{type}_{now}", Required = false, HelpText = "Template of build folder / file prefix")]
            public string WorkDir { get; set; }
        }

        private static void Run(Options option)
        {
            var sw = Stopwatch.StartNew();

            var nowStr = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

            var collection = new Collection();
            bool isSucceed = collection.LoadDir(option.InDir, "collection.txt");
            if (!isSucceed)
            {
                Utils.Logger?.Error("Failed to load input dir: {dir}", option.InDir);
                return;
            }

            collection.DumpCollectionJson(Utils.GetAppRelativePath(option.WorkDir
                .Replace("{collection}", collection.Id)
                .Replace("{type}", "json")
                .Replace("{now}", nowStr)
                ) + ".json");

            var resourcePackPath = Utils.GetAppRelativePath(option.WorkDir
                .Replace("{collection}", collection.Id)
                .Replace("{type}", "resource")
                .Replace("{now}", nowStr)
                );
            isSucceed = collection.BuildResourcePack(option.InDir, resourcePackPath);
            if (!isSucceed)
            {
                Utils.Logger?.Error("Failed to build resource pack: {path}", resourcePackPath);
                return;
            }

            var dataPackPath = Utils.GetAppRelativePath(option.WorkDir
                .Replace("{collection}", collection.Id)
                .Replace("{type}", "data")
                .Replace("{now}", nowStr)
                );
            isSucceed = collection.BuildDataPack(option.InDir, dataPackPath);
            if (!isSucceed)
            {
                Utils.Logger?.Error("Failed to build data pack: {path}", dataPackPath);
                return;
            }

            sw.Stop();
            Utils.Logger?.Information("Succeed in {sw} ms", sw.ElapsedMilliseconds);
        }
    }
}
