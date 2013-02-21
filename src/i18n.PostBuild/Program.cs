using System;

namespace i18n.PostBuild
{
    class Program
    {
        static void Main(string[] args)
        {
            string solutionPath;

            if (args.Length == 0)
            {
                //Si no ens indiquen res suposem que el directori del projecte està en el directori pare on s'està executant aquest programa
                string currentPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                solutionPath = System.IO.Directory.GetParent(currentPath).FullName;
            }
            else
            {
                solutionPath = args[0];
                solutionPath = solutionPath.Trim(new[] { '\"' });
            }

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

            new PostBuildTask().Execute(solutionPath, gettext, msgmerge, inputPaths);
        }
    }
}
