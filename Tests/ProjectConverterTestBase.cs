﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ICSharpCode.CodeConverter;
using ICSharpCode.CodeConverter.CSharp;
using ICSharpCode.CodeConverter.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualBasic.FileIO;
using Xunit;
using SearchOption = System.IO.SearchOption;

namespace CodeConverter.Tests
{
    public class ProjectConverterTestBase
    {
        /// <summary>
        /// Only commit the results of this if you've verified the new conversion is correct.
        /// Copying the whole conversion result (not just the sln and proj files) over the source project should yield a compiling solution.
        /// </summary>
        private bool _writeNewCharacterization = false;

        public void ConvertProjectsWhere<TLanguageConversion>(Func<Project, bool> shouldConvertProject, [CallerMemberName] string testName = "") where TLanguageConversion : ILanguageConversion, new()
        {
            using (var workspace = MSBuildWorkspace.Create())
            {
                var solutionDir = Path.Combine(GetTestDataDirectory(), "CharacterizationTestSolution");
                var solutionFile = Path.Combine(solutionDir, "CharacterizationTestSolution.sln");

                var solution = workspace.OpenSolutionAsync(solutionFile).GetAwaiter().GetResult();
                var languageNameToConvert = typeof(TLanguageConversion) == typeof(VBToCSConversion)
                    ? LanguageNames.VisualBasic
                    : LanguageNames.CSharp;
                var projectsToConvert = solution.Projects.Where(p => p.Language == languageNameToConvert && shouldConvertProject(p)).ToArray();
                var conversionResults = SolutionConverter.CreateFor<TLanguageConversion>(projectsToConvert).Convert().ToDictionary(c => c.TargetPathOrNull);
                var expectedResultDirectory = GetExpectedResultDirectory<TLanguageConversion>(testName);
                
                try {

                    var expectedFiles = expectedResultDirectory.GetFiles("*", SearchOption.AllDirectories);
                    foreach (var expectedFile in expectedFiles) {
                        AssertFileEqual(conversionResults, expectedResultDirectory, expectedFile, solutionDir);
                    }
                } catch (Exception e) when (_writeNewCharacterization) {
                    Console.Error.WriteLine(e);
                    foreach (var conversionResult in conversionResults
                        .Where(f => f.Key.EndsWith("proj") || f.Key.EndsWith(".sln"))
                    ) {
                        var expectedFilePath =
                            conversionResult.Key.Replace(solutionDir, expectedResultDirectory.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(expectedFilePath));
                        File.WriteAllText(expectedFilePath, conversionResult.Value.ConvertedCode);
                    }
                }
            }
        }

        private void AssertFileEqual(Dictionary<string, ConversionResult> conversionResults,
            DirectoryInfo expectedResultDirectory,
            FileInfo expectedFile,
            string actualSolutionDir)
        {
            var actualFilePath = expectedFile.FullName.Replace(expectedResultDirectory.FullName, actualSolutionDir);
            Assert.True(conversionResults.ContainsKey(actualFilePath),
                expectedFile.Name + " is missing from the conversion result");

            var expectedText = Utils.HomogenizeEol(File.ReadAllText(expectedFile.FullName));
            var conversionResult = conversionResults[actualFilePath];
            var actualText =
                Utils.HomogenizeEol(conversionResult.ConvertedCode ?? "" + conversionResult.GetExceptionsAsString() ?? "");

            Assert.Equal(expectedText, actualText);
        }

        private static DirectoryInfo GetExpectedResultDirectory<TLanguageConversion>(string testName) where TLanguageConversion : ILanguageConversion, new()
        {
            var combine = Path.Combine(GetTestDataDirectory(), typeof(TLanguageConversion).Name, testName);
            return new DirectoryInfo(combine);
        }

        private static string GetTestDataDirectory()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var solutionDir = new FileInfo(new Uri(assembly.CodeBase).LocalPath).Directory?.Parent?.Parent?.Parent ??
                              throw new InvalidOperationException(assembly.CodeBase);
            return Path.Combine(solutionDir.FullName, "TestData");
        }
    }
}