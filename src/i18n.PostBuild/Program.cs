﻿using System;

namespace i18n.PostBuild
{
    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length == 0)
            {
                Console.WriteLine("This post build task requires passing in the $(ProjectDirectory) path");
                return;
            }

            var path = args[0];
            path = path.Trim(new[] {'\"'});

            string gettext = null;
            string msgmerge = null;
            string[] inputPaths = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("gettext:", StringComparison.InvariantCultureIgnoreCase))
                    gettext = args[i].Substring(8);

                if (args[i].StartsWith("msgmerge:", StringComparison.InvariantCultureIgnoreCase))
                    msgmerge = args[i].Substring(9);
                if (args[i].StartsWith("inputpaths:", StringComparison.InvariantCultureIgnoreCase))
                    inputPaths = args[i].Substring(11).Split(',');
            }

            new PostBuildTask().Execute(path, gettext, msgmerge, inputPaths);
        }
    }
}
