// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace PrimalEditor.GameProject;

[DataContract]
class ProjectTemplate
{
    [DataMember]
    public string ProjectType { get; set; }
    [DataMember]
    public string ProjectFile { get; set; }
    [DataMember]
    public List<string> Folders { get; set; }

    public byte[] Icon { get; set; }
    public byte[] Screenshot { get; set; }
    public string IconFilePath { get; set; }
    public string ScreenshotFilePath { get; set; }
    public string ProjectFilePath { get; set; }
    public string TemplatePath { get; set; }
}

class NewProject : ViewModelBase
{
    public string ProjectName
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ValidateProjectPath();
                OnPropertyChanged(nameof(ProjectName));
            }
        }
    } = "NewProject";

    public string ProjectPath
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ValidateProjectPath();
                OnPropertyChanged(nameof(ProjectPath));
            }
        }
    } = $@"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\PrimalProjects\";

    public bool IsValid
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsValid));
            }
        }
    } = true;

    public string ErrorMsg
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ErrorMsg));
            }
        }
    }

    private readonly ObservableCollection<ProjectTemplate> _projectTemplates = [];
    public ReadOnlyObservableCollection<ProjectTemplate> ProjectTemplates
    { get; }

    private bool ValidateProjectPath()
    {
        var path = ProjectPath;
        if (!Path.EndsInDirectorySeparator(path)) path += @"\";
        path += $@"{ProjectName}\";
        var nameRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        IsValid = false;
        if (string.IsNullOrEmpty(ProjectName.Trim()))
        {
            ErrorMsg = "Type in a project name.";
        }
        else if (!nameRegex.IsMatch(ProjectName))
        {
            ErrorMsg = "Invalid character(s) used in project name.";
        }
        else if (string.IsNullOrEmpty(ProjectPath.Trim()))
        {
            ErrorMsg = "Select a valid project folder.";
        }
        else if (ProjectPath.IndexOfAny(Path.GetInvalidPathChars()) != -1)
        {
            ErrorMsg = "Invalid character(s) used in project path.";
        }
        else if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            ErrorMsg = "Selected project folder already exists and is not empty.";
        }
        else
        {
            ErrorMsg = string.Empty;
            IsValid = true;
        }

        return IsValid;
    }

    public string CreateProject(ProjectTemplate template)
    {
        if (!ValidateProjectPath())
        {
            return string.Empty;
        }

        ProjectName = ProjectName.Trim();
        ProjectPath = ProjectPath.Trim();

        if (!Path.EndsInDirectorySeparator(ProjectPath)) ProjectPath += @"\";
        var path = $@"{ProjectPath}{ProjectName}\";

        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            foreach (var folder in template.Folders)
            {
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), folder)));
            }
            var dirInfo = new DirectoryInfo(path + @".Primal\");
            dirInfo.Attributes |= FileAttributes.Hidden;
            File.Copy(template.IconFilePath, Path.GetFullPath(Path.Combine(dirInfo.FullName, "Icon.png")));
            File.Copy(template.ScreenshotFilePath, Path.GetFullPath(Path.Combine(dirInfo.FullName, "Screenshot.png")));

            var projectXml = File.ReadAllText(template.ProjectFilePath);
            projectXml = string.Format(projectXml, ProjectName, path);
            var projectPath = Path.GetFullPath(Path.Combine(path, $"{ProjectName}{Project.Extension}"));
            File.WriteAllText(projectPath, projectXml);

            CreateMSVCSolution(template, path);

            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to create {ProjectName}");
            throw;
        }
    }

    private void CreateMSVCSolution(ProjectTemplate template, string projectPath)
    {
        Debug.Assert(File.Exists(Path.Combine(template.TemplatePath, "MSVCSolution")));
        Debug.Assert(File.Exists(Path.Combine(template.TemplatePath, "MSVCProject")));

        var engineAPIPath = @"$(PRIMAL_ENGINE)Engine\EngineAPI\";
        //TODO: check if engineAPIPath exists.

        var _0 = ProjectName;
        var _1 = "{" + Guid.NewGuid().ToString().ToUpper() + "}";
        var _2 = engineAPIPath;
        var _3 = "$(PRIMAL_ENGINE)";

        var solution = File.ReadAllText(Path.Combine(template.TemplatePath, "MSVCSolution"));
        solution = string.Format(solution, _0, _1, "{" + Guid.NewGuid().ToString().ToUpper() + "}");
        File.WriteAllText(Path.GetFullPath(Path.Combine(projectPath, $"{_0}.sln")), solution);
        var project = File.ReadAllText(Path.Combine(template.TemplatePath, "MSVCProject"));
        project = string.Format(project, _0, _1, _2, _3);
        File.WriteAllText(Path.GetFullPath(Path.Combine(projectPath, $@"GameCode\{_0}.vcxproj")), project);
    }

    public NewProject()
    {
        ProjectTemplates = new ReadOnlyObservableCollection<ProjectTemplate>(_projectTemplates);
        try
        {
            var templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Resources\ProjectTemplates\");
            var templatesFiles = Directory.GetFiles(templatesPath, "template.xml", SearchOption.AllDirectories);
            Debug.Assert(templatesFiles.Any());
            foreach (var file in templatesFiles)
            {
                var template = Serializer.FromFile<ProjectTemplate>(file);
                template.TemplatePath = Path.GetDirectoryName(file);
                template.IconFilePath = Path.GetFullPath(Path.Combine(template.TemplatePath, "Icon.png"));
                template.Icon = File.ReadAllBytes(template.IconFilePath);
                template.ScreenshotFilePath = Path.GetFullPath(Path.Combine(template.TemplatePath, "Screenshot.png"));
                template.Screenshot = File.ReadAllBytes(template.ScreenshotFilePath);
                template.ProjectFilePath = Path.GetFullPath(Path.Combine(template.TemplatePath, template.ProjectFile));

                _projectTemplates.Add(template);
            }
            ValidateProjectPath();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to read project templates");
            throw;
        }
    }
}
