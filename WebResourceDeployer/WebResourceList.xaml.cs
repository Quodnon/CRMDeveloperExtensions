﻿using CrmConnectionWindow;
using EnvDTE;
using EnvDTE80;
using InfoWindow;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using OutputLogger;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using WebResourceDeployer.Models;
using Window = EnvDTE.Window;

namespace WebResourceDeployer
{
    public partial class WebResourceList
    {
        //All DTE objects need to be declared here or else things stop working
        private readonly DTE _dte;
        private readonly Solution _solution;
        private readonly Events _events;
        private readonly Events2 _events2;
        private readonly SolutionEvents _solutionEvents;
        private readonly ProjectItemsEvents _projectItemsEvents;
        private Projects _projects;
        private IVsSolutionEvents _vsSolutionEvents;
        private readonly IVsSolution _vsSolution;
        private uint _solutionEventsCookie;

        private List<WebResourceItem> _movedWebResourceItems;
        private List<string> _movedBoundFiles;
        private uint _movedItemid;
        private CrmConn _selectedConn;
        private Project _selectedProject;
        private bool _projectEventsRegistered;
        private bool _connectionAdded;
        private readonly Logger _logger;

        private readonly FieldInfo _menuDropAlignmentField;

        readonly string[] _extensions = { "HTM", "HTML", "CSS", "JS", "XML", "PNG", "JPG", "GIF", "XAP", "XSL", "XSLT", "ICO", "TS" };

        public WebResourceList()
        {
            InitializeComponent();

            _logger = new Logger();

            _dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (_dte == null)
                return;

            _solution = _dte.Solution;
            if (_solution == null)
                return;

            _events = _dte.Events;
            var windowEvents = _events.WindowEvents;
            windowEvents.WindowActivated += WindowEventsOnWindowActivated;
            _solutionEvents = _events.SolutionEvents;
            _solutionEvents.BeforeClosing += BeforeSolutionClosing;
            _solutionEvents.Opened += SolutionOpened;
            _solutionEvents.BeforeClosing += SolutionBeforeClosing;
            _solutionEvents.ProjectAdded += SolutionProjectAdded;
            _solutionEvents.ProjectRemoved += SolutionProjectRemoved;
            _solutionEvents.ProjectRenamed += SolutionProjectRenamed;

            _events2 = (Events2)_dte.Events;
            _projectItemsEvents = _events2.ProjectItemsEvents;
            _projectItemsEvents.ItemRenamed += ProjectItemRenamed;

            _vsSolutionEvents = new VsSolutionEvents(this);
            _vsSolution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution));
            _vsSolution.AdviseSolutionEvents(_vsSolutionEvents, out _solutionEventsCookie);

            //Fix for Tablet/Touchscreen left-right menu
            _menuDropAlignmentField = typeof(SystemParameters).GetField("_menuDropAlignment", BindingFlags.NonPublic | BindingFlags.Static);
            System.Diagnostics.Debug.Assert(_menuDropAlignmentField != null);
            EnsureStandardPopupAlignment();
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        }

        private void SystemParameters_StaticPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            EnsureStandardPopupAlignment();
        }

        private void EnsureStandardPopupAlignment()
        {
            if (SystemParameters.MenuDropAlignment && _menuDropAlignmentField != null)
            {
                _menuDropAlignmentField.SetValue(null, false);
            }
        }

        private void WindowEventsOnWindowActivated(Window gotFocus, Window lostFocus)
        {
            if (_projects == null)
                _projects = _dte.Solution.Projects;

            //No solution loaded
            if (_solution.Count == 0)
            {
                ResetForm();
                return;
            }

            //Lost focus
            if (gotFocus.Caption != WebResourceDeployer.Resources.ResourceManager.GetString("ToolWindowTitle")) return;

            Projects.IsEnabled = true;
            AddConnection.IsEnabled = true;
            Connections.IsEnabled = true;

            foreach (Project project in _projects)
            {
                SolutionProjectAdded(project);
            }

            if (!_projectEventsRegistered)
            {
                RegisterProjectEvents();
                _projectEventsRegistered = true;
            }
        }

        private void RegisterProjectEvents()
        {
            //Manually register the OnAfterOpenProject event on the existing projects as they are already opened by the time the event would normally be registered
            foreach (Project project in _projects)
            {
                IVsHierarchy projectHierarchy;
                if (_vsSolution.GetProjectOfUniqueName(project.UniqueName, out projectHierarchy) != VSConstants.S_OK)
                    continue;

                IVsSolutionEvents vsSolutionEvents = new VsSolutionEvents(this);
                vsSolutionEvents.OnAfterOpenProject(projectHierarchy, 1);
            }
        }

        private void SolutionBeforeClosing()
        {
            //Close the Web Resource Deployer window - forces having to reopen for a new solution
            foreach (Window window in _dte.Windows)
            {
                if (window.Caption != WebResourceDeployer.Resources.ResourceManager.GetString("ToolWindowTitle")) continue;

                ResetForm();
                _logger.DeleteOutputWindow();
                window.Close();
                return;
            }
        }

        private void ProjectItemRenamed(ProjectItem projectItem, string oldName)
        {
            if (!IsItemInSelectedProject(projectItem)) return;

            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            if (webResources == null) return;

            var fullname = projectItem.FileNames[1];
            var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
            if (projectPath == null) return;

            if (projectItem.Kind.ToUpper() == "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}") //File
            {
                var newItemName = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");

                if (projectItem.Name == null) return;

                var oldItemName = newItemName.Replace(Path.GetFileName(projectItem.Name), oldName).Replace("//", "/");

                foreach (var webResourceItem in webResources)
                {
                    string boundFile = webResourceItem.BoundFile;
                    webResourceItem.BoundFile = boundFile;

                    if (webResourceItem.BoundFile != oldItemName) continue;

                    webResourceItem.BoundFile = newItemName;
                }
            }
            else if (projectItem.Kind.ToUpper() == "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}") //Folder
            {
                foreach (WebResourceItem webResourceItem in webResources)
                {
                    var newItemPath = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");
                    if (newItemPath.EndsWith("/"))
                        newItemPath = newItemPath.Remove(newItemPath.Length - 1, 1);

                    if (projectItem.Name == null) return;

                    int index = newItemPath.LastIndexOf(projectItem.Name, StringComparison.Ordinal);
                    if (index == -1) continue;

                    var oldItemPath = newItemPath.Remove(index, projectItem.Name.Length).Insert(index, oldName);

                    if (!string.IsNullOrEmpty(webResourceItem.BoundFile) && webResourceItem.BoundFile.StartsWith(oldItemPath))
                        webResourceItem.BoundFile = webResourceItem.BoundFile.Replace(oldItemPath, newItemPath);
                }
            }

            ProjectFileList.ItemsSource = GetProjectFiles(_selectedProject.Name);
        }

        public void ProjectItemRemoved(ProjectItem projectItem, uint itemid)
        {
            if (!IsItemInSelectedProject(projectItem)) return;

            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            if (webResources == null) return;

            var fullname = projectItem.FileNames[1];
            var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
            if (projectPath == null) return;

            if (projectItem.Kind.ToUpper() == "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}") //File
            {
                var name = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");

                foreach (WebResourceItem webResourceItem in webResources)
                {
                    if (!string.IsNullOrEmpty(webResourceItem.BoundFile) && webResourceItem.BoundFile == name)
                    {
                        _movedWebResourceItems = new List<WebResourceItem> { webResourceItem };
                        _movedItemid = itemid;
                        webResourceItem.BoundFile = null;
                        webResourceItem.Publish = false;
                    }

                    if (!string.IsNullOrEmpty(webResourceItem.BoundFile))
                    {
                        var boundFile = webResourceItem.BoundFile;
                        bool publish = webResourceItem.Publish;
                        webResourceItem.BoundFile = boundFile;
                        webResourceItem.Publish = publish;
                    }

                    SetPublishAll();
                }

                ObservableCollection<ComboBoxItem> projectFiles =
                    (ObservableCollection<ComboBoxItem>)ProjectFileList.ItemsSource;
                foreach (ComboBoxItem comboBoxItem in projectFiles.ToList())
                {
                    if (comboBoxItem.Content.ToString() == name)
                        projectFiles.Remove(comboBoxItem);
                }

                //If there is only 1 item left it must be the empty item - so remove it
                if (projectFiles.ToList().Count == 1)
                    projectFiles.RemoveAt(0);
            }
            else if (projectItem.Kind.ToUpper() == "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}") //Folder
            {
                _movedWebResourceItems = new List<WebResourceItem>();
                _movedBoundFiles = new List<string>();
                var name = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");
                foreach (WebResourceItem webResourceItem in webResources)
                {
                    foreach (MenuItem item in webResourceItem.ProjectFolders.ToList())
                    {
                        string folder = item.Header + "/";
                        if (folder.StartsWith(name))
                            webResourceItem.ProjectFolders.Remove(item);
                    }

                    if (!string.IsNullOrEmpty(webResourceItem.BoundFile) && webResourceItem.BoundFile.StartsWith(name))
                    {
                        _movedWebResourceItems.Add(webResourceItem);
                        _movedBoundFiles.Add(webResourceItem.BoundFile);
                        _movedItemid = itemid;
                        webResourceItem.BoundFile = null;
                    }
                }

                ObservableCollection<ComboBoxItem> projectFiles =
                    (ObservableCollection<ComboBoxItem>)ProjectFileList.ItemsSource;
                foreach (ComboBoxItem comboBoxItem in projectFiles.ToList())
                {
                    if (comboBoxItem.Content.ToString().StartsWith(name))
                        projectFiles.Remove(comboBoxItem);
                }

                ProjectFileList.ItemsSource = projectFiles;
            }
        }

        public void ProjectItemAdded(ProjectItem projectItem, uint itemid)
        {
            if (!IsItemInSelectedProject(projectItem)) return;

            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            if (webResources == null) return;

            if (projectItem.Kind.ToUpper() == "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}") //File
            {
                var fullname = projectItem.FileNames[1];
                //Don't want to include files being added here from the temp folder during a diff operation
                if (fullname.ToUpper().Contains(Path.GetTempPath().ToUpper()))
                    return;

                var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
                if (projectPath == null) return;

                foreach (WebResourceItem webResourceItem in webResources)
                {
                    string boundFile = webResourceItem.BoundFile;
                    bool publish = webResourceItem.Publish;
                    webResourceItem.BoundFile = boundFile;
                    webResourceItem.Publish = publish;
                }

                //Item was moved inside the project
                if (itemid == _movedItemid)
                {
                    foreach (WebResourceItem webResourceItem in _movedWebResourceItems)
                    {
                        var boundName = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");
                        webResourceItem.BoundFile = boundName;
                    }
                    _movedItemid = 0;
                }

                ProjectFileList.ItemsSource = GetProjectFiles(_selectedProject.Name);
            }
            else if (projectItem.Kind.ToUpper() == "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}") //Folder
            {
                ObservableCollection<MenuItem> projectFolders = GetProjectFolders(_selectedProject.Name);

                foreach (WebResourceItem webResourceItem in webResources)
                {
                    webResourceItem.ProjectFolders = projectFolders;
                }

                //Item was moved inside the project
                if (itemid == _movedItemid)
                {
                    var fullname = projectItem.FileNames[1];
                    var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
                    if (projectPath == null) return;
                    int i = 0;
                    foreach (WebResourceItem webResourceItem in _movedWebResourceItems)
                    {
                        string[] parts1 = _movedBoundFiles[i].Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        string[] parts2 = fullname.Replace(projectPath, String.Empty).Replace("\\", "/").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                        string last1 = parts1[parts1.Length - 2]; //last part of the path
                        //Build the new file path
                        string newPath = "/";
                        foreach (string s in parts2)
                        {
                            if (s != last1)
                                newPath += s + "/";
                        }
                        newPath += last1 + "/" + parts1[parts1.Length - 1];
                        webResourceItem.BoundFile = newPath;
                        i++;
                    }
                    _movedItemid = 0;
                }
            }
        }

        private bool IsItemInSelectedProject(ProjectItem projectItem)
        {
            Project project = projectItem.ContainingProject;
            return _selectedProject == project;
        }

        private void SolutionOpened()
        {
            _projects = _dte.Solution.Projects;
        }

        private void SolutionProjectRenamed(Project project, string oldName)
        {
            string name = Path.GetFileNameWithoutExtension(oldName);
            foreach (ComboBoxItem comboBoxItem in Projects.Items)
            {
                if (string.IsNullOrEmpty(comboBoxItem.Content.ToString())) continue;
                if (name != null && comboBoxItem.Content.ToString().ToUpper() != name.ToUpper()) continue;

                comboBoxItem.Content = project.Name;
            }

            _projects = _dte.Solution.Projects;
        }

        private void SolutionProjectRemoved(Project project)
        {
            foreach (ComboBoxItem comboBoxItem in Projects.Items)
            {
                if (string.IsNullOrEmpty(comboBoxItem.Content.ToString())) continue;
                if (comboBoxItem.Content.ToString().ToUpper() != project.Name.ToUpper()) continue;

                Projects.Items.Remove(comboBoxItem);
                break;
            }

            if (_selectedProject != null)
            {
                if (_selectedProject.FullName == project.FullName)
                {
                    WebResourceGrid.ItemsSource = null;
                    Connections.ItemsSource = null;
                    Connections.Items.Clear();
                    Connections.IsEnabled = false;
                    AddConnection.IsEnabled = false;
                    Publish.IsEnabled = false;
                    Customizations.IsEnabled = false;
                    Solutions.IsEnabled = false;
                    SolutionList.IsEnabled = false;
                    AddWebResource.IsEnabled = false;
                }
            }

            _projects = _dte.Solution.Projects;
        }

        private void SolutionProjectAdded(Project project)
        {
            //Don't want to include the VS Miscellaneous Files Project - which appears occasionally and during a diff operation
            if (project.Name.ToUpper() == "MISCELLANEOUS FILES")
                return;

            bool addProject = true;
            foreach (ComboBoxItem projectItem in Projects.Items)
            {
                if (projectItem.Content.ToString().ToUpper() != project.Name.ToUpper()) continue;

                addProject = false;
                break;
            }

            if (addProject)
            {
                ComboBoxItem item = new ComboBoxItem() { Content = project.Name, Tag = project };
                Projects.Items.Add(item);
            }

            if (Projects.SelectedIndex == -1)
                Projects.SelectedIndex = 0;

            _projects = _dte.Solution.Projects;
        }

        private void BeforeSolutionClosing()
        {
            ResetForm();
        }

        private void ResetForm()
        {
            WebResourceGrid.ItemsSource = null;
            Connections.ItemsSource = null;
            Connections.Items.Clear();
            Connections.IsEnabled = false;
            Projects.ItemsSource = null;
            Projects.Items.Clear();
            Projects.IsEnabled = false;
            AddConnection.IsEnabled = false;
            Publish.IsEnabled = false;
            Customizations.IsEnabled = false;
            Solutions.IsEnabled = false;
            SolutionList.IsEnabled = false;
            AddWebResource.IsEnabled = false;
        }

        private async void AddConnection_Click(object sender, RoutedEventArgs e)
        {
            Connection connection = new Connection(null, null);
            bool? result = connection.ShowDialog();

            if (!result.HasValue || !result.Value) return;

            var configExists = ConfigFileExists(_selectedProject);
            if (!configExists)
                CreateConfigFile(_selectedProject);

            Expander.IsExpanded = false;

            bool change = AddOrUpdateConnection(_selectedProject, connection.ConnectionName, connection.ConnectionString, connection.OrgId, connection.Version, true);

            if (!change) return;

            GetConnections();
            foreach (CrmConn conn in Connections.Items)
            {
                if (conn.Name != connection.ConnectionName) continue;

                Connections.SelectedItem = conn;
                await GetSolutions();
                await GetWebResources(connection.ConnectionString);
                break;
            }
        }

        private bool AddOrUpdateConnection(Project vsProject, string connectionName, string connString, string orgId, string versionNum, bool showPrompt)
        {
            try
            {
                var path = Path.GetDirectoryName(vsProject.FullName);
                if (!ConfigFileExists(vsProject))
                {
                    _logger.WriteToOutputWindow("Error Adding Or Updating Connection: Missing CRMDeveloperExtensions.config File", Logger.MessageType.Error);
                    return false;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(path + "\\CRMDeveloperExtensions.config");

                //Check if connection alredy exists for project
                XmlNodeList connectionStrings = doc.GetElementsByTagName("ConnectionString");
                if (connectionStrings.Count > 0)
                {
                    foreach (XmlNode node in connectionStrings)
                    {
                        string decodedString = DecodeString(node.InnerText);
                        if (decodedString != connString) continue;

                        if (showPrompt)
                        {
                            MessageBoxResult result = MessageBox.Show("Update Connection?", "Connection Already Added",
                                MessageBoxButton.YesNo);

                            //Update existing connection
                            if (result != MessageBoxResult.Yes)
                                return false;
                        }

                        XmlNode connectionU = node.ParentNode;
                        if (connectionU != null)
                        {
                            XmlNode nameNode = connectionU["Name"];
                            if (nameNode != null)
                                nameNode.InnerText = connectionName;
                            XmlNode versionNode = connectionU["Version"];
                            if (versionNode != null)
                                versionNode.InnerText = versionNum;
                        }

                        doc.Save(path + "\\CRMDeveloperExtensions.config");
                        return true;
                    }
                }

                //Add the connection elements
                XmlNodeList connections = doc.GetElementsByTagName("Connections");
                XmlElement connection = doc.CreateElement("Connection");
                XmlElement name = doc.CreateElement("Name");
                name.InnerText = connectionName;
                connection.AppendChild(name);
                XmlElement org = doc.CreateElement("OrgId");
                org.InnerText = orgId;
                connection.AppendChild(org);
                XmlElement connectionString = doc.CreateElement("ConnectionString");
                connectionString.InnerText = EncodeString(connString);
                connection.AppendChild(connectionString);
                XmlElement version = doc.CreateElement("Version");
                version.InnerText = versionNum;
                connection.AppendChild(version);
                XmlElement selected = doc.CreateElement("Selected");
                selected.InnerText = "True";
                connection.AppendChild(selected);
                connections[0].AppendChild(connection);

                _connectionAdded = true;

                doc.Save(path + "\\CRMDeveloperExtensions.config");
                return true;
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Adding Or Updating Connection: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
                return false;
            }
        }

        private void CreateConfigFile(Project vsProject)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                XmlElement webResourceDeployer = doc.CreateElement("WebResourceDeployer");
                XmlElement connections = doc.CreateElement("Connections");
                XmlElement files = doc.CreateElement("Files");
                webResourceDeployer.AppendChild(connections);
                webResourceDeployer.AppendChild(files);
                doc.AppendChild(webResourceDeployer);

                var path = Path.GetDirectoryName(vsProject.FullName);
                doc.Save(path + "/CRMDeveloperExtensions.config");
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Creating Config File: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
            }
        }

        private bool ConfigFileExists(Project project)
        {
            var path = Path.GetDirectoryName(project.FullName);
            return File.Exists(path + "/CRMDeveloperExtensions.config");
        }

        private Project GetProjectByName(string projectName)
        {
            foreach (Project project in _projects)
            {
                if (project.Name != projectName) continue;

                return project;
            }

            return null;
        }

        private ObservableCollection<ComboBoxItem> GetProjectFiles(string projectName)
        {
            ObservableCollection<ComboBoxItem> projectFiles = new ObservableCollection<ComboBoxItem>();
            Project project = GetProjectByName(projectName);
            if (project == null)
                return projectFiles;

            var projectItems = project.ProjectItems;
            for (int i = 1; i <= projectItems.Count; i++)
            {
                var files = GetFiles(projectItems.Item(i), String.Empty);
                foreach (var comboBoxItem in files)
                {
                    projectFiles.Add(comboBoxItem);
                }
            }

            if (projectFiles.Count > 0)
                projectFiles.Insert(0, new ComboBoxItem() { Content = String.Empty });

            return projectFiles;
        }

        private ObservableCollection<ComboBoxItem> GetFiles(ProjectItem projectItem, string path)
        {
            ObservableCollection<ComboBoxItem> projectFiles = new ObservableCollection<ComboBoxItem>();
            if (projectItem.Kind != "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}") // VS Folder 
            {
                string ex = Path.GetExtension(projectItem.Name);
                if (ex == null || (!_extensions.Contains(ex.Replace(".", String.Empty).ToUpper()) && !string.IsNullOrEmpty(ex) && ex.ToUpper() != ".BUNDLE"))
                    return projectFiles;

                //Don't add .bundle files
                if (ex.ToUpper() != ".BUNDLE")
                    projectFiles.Add(new ComboBoxItem() { Content = path + "/" + projectItem.Name, Tag = projectItem });

                if (projectItem.ProjectItems.Count <= 0)
                    return projectFiles;

                //Handle minified/bundled files that appear under other files in the project
                for (int i = 1; i <= projectItem.ProjectItems.Count; i++)
                {
                    ObservableCollection<ComboBoxItem> subFiles = GetFiles(projectItem.ProjectItems.Item(i), path);
                    foreach (ComboBoxItem comboBoxItem in subFiles)
                    {
                        projectFiles.Add(comboBoxItem);
                    }
                }
            }
            else
            {
                for (int i = 1; i <= projectItem.ProjectItems.Count; i++)
                {
                    //Ignore TypeScript typings folders
                    if (projectItem.Name.ToUpper() == "TYPINGS") continue;

                    var files = GetFiles(projectItem.ProjectItems.Item(i), path + "/" + projectItem.Name);
                    foreach (var comboBoxItem in files)
                    {
                        projectFiles.Add(comboBoxItem);
                    }
                }
            }
            return projectFiles;
        }

        private ObservableCollection<MenuItem> GetProjectFolders(string projectName)
        {
            List<string> projectFolders = new List<string>();
            ObservableCollection<MenuItem> projectMenuItems = new ObservableCollection<MenuItem>();
            Project project = GetProjectByName(projectName);
            if (project == null)
                return projectMenuItems;

            var projectItems = project.ProjectItems;
            for (int i = 1; i <= projectItems.Count; i++)
            {
                var folders = GetFolders(projectItems.Item(i), String.Empty);
                foreach (string folder in folders)
                {
                    if (folder.ToUpper() == "/PROPERTIES") continue; //Don't add the project Properties folder
                    if (folder.ToUpper().StartsWith("/MY PROJECT")) continue; //Don't add the VB project My Project folders
                    projectFolders.Add(folder);
                }
            }

            projectFolders.Insert(0, "/");

            foreach (string projectFolder in projectFolders)
            {
                MenuItem item = new MenuItem
                {
                    Header = projectFolder
                };
                item.Click += DownloadWebResourceToFolder;

                projectMenuItems.Add(item);
            }

            return projectMenuItems;
        }

        private ObservableCollection<string> GetFolders(ProjectItem projectItem, string path)
        {
            ObservableCollection<string> projectFolders = new ObservableCollection<string>();
            if (projectItem.Kind == "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}") // VS Folder 
            {
                projectFolders.Add(path + "/" + projectItem.Name);
                for (int i = 1; i <= projectItem.ProjectItems.Count; i++)
                {
                    var folders = GetFolders(projectItem.ProjectItems.Item(i), path + "/" + projectItem.Name);
                    foreach (string folder in folders)
                    {
                        projectFolders.Add(folder);
                    }
                }
            }
            return projectFolders;
        }

        private void GetConnections()
        {
            Connections.ItemsSource = null;

            var path = Path.GetDirectoryName(_selectedProject.FullName);
            XmlDocument doc = new XmlDocument();

            if (!ConfigFileExists(_selectedProject))
            {
                _logger.WriteToOutputWindow("Error Retrieving Connections From Config File: Missing CRMDeveloperExtensions.config file", Logger.MessageType.Error);
                return;
            }

            doc.Load(path + "\\CRMDeveloperExtensions.config");
            XmlNodeList connections = doc.GetElementsByTagName("Connection");
            if (connections.Count == 0) return;

            List<CrmConn> crmConnections = new List<CrmConn>();

            foreach (XmlNode node in connections)
            {
                CrmConn conn = new CrmConn();
                XmlNode nameNode = node["Name"];
                if (nameNode != null)
                    conn.Name = nameNode.InnerText;
                XmlNode connectionStringNode = node["ConnectionString"];
                if (connectionStringNode != null)
                    conn.ConnectionString = DecodeString(connectionStringNode.InnerText);
                XmlNode orgIdNode = node["OrgId"];
                if (orgIdNode != null)
                    conn.OrgId = orgIdNode.InnerText;
                XmlNode versionNode = node["Version"];
                if (versionNode != null)
                    conn.Version = versionNode.InnerText;

                crmConnections.Add(conn);
            }

            Connections.ItemsSource = crmConnections;

            if (Connections.SelectedIndex == -1 && crmConnections.Count > 0)
                Connections.SelectedIndex = 0;
        }

        private static string EncodedImage(string filePath, string extension)
        {
            string encodedImage;

            if (extension.ToUpper() == ".ICO")
            {
                System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);

                using (MemoryStream ms = new MemoryStream())
                {
                    if (icon != null) icon.Save(ms);
                    byte[] imageBytes = ms.ToArray();
                    encodedImage = Convert.ToBase64String(imageBytes);
                }

                return encodedImage;
            }

            System.Drawing.Image image = System.Drawing.Image.FromFile(filePath, true);

            ImageFormat format = null;
            switch (extension.ToUpper())
            {
                case ".GIF":
                    format = ImageFormat.Gif;
                    break;
                case ".JPG":
                    format = ImageFormat.Jpeg;
                    break;
                case ".PNG":
                    format = ImageFormat.Png;
                    break;
            }

            if (format == null)
                return null;

            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, format);
                byte[] imageBytes = ms.ToArray();
                encodedImage = Convert.ToBase64String(imageBytes);
            }
            return encodedImage;
        }

        private string EncodeString(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private string DecodeString(string value)
        {
            byte[] data = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(data);
        }

        private byte[] DecodeWebResource(string value)
        {
            return Convert.FromBase64String(value);
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConn == null) return;

            string connString = _selectedConn.ConnectionString;
            if (string.IsNullOrEmpty(connString)) return;

            UpdateSelectedConnection(true);

            WebResourceType.SelectedIndex = -1;
            ShowManaged.IsChecked = false;

            Expander.IsExpanded = false;
            Customizations.IsEnabled = true;
            Solutions.IsEnabled = true;
            SolutionList.IsEnabled = true;

            await GetSolutions();
            await GetWebResources(connString);
        }

        private async Task<bool> GetWebResources(string connString)
        {
            string projectName = _selectedProject.Name;
            CrmConnection connection = CrmConnection.Parse(connString);

            _dte.StatusBar.Text = "Connecting to CRM and getting web resources...";
            _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);
            LockMessage.Content = "Working...";
            LockOverlay.Visibility = Visibility.Visible;

            EntityCollection results = await System.Threading.Tasks.Task.Run(() => RetrieveWebResourcesFromCrm(connection));
            if (results == null)
            {
                _dte.StatusBar.Clear();
                LockOverlay.Visibility = Visibility.Hidden;
                MessageBox.Show("Error Retrieving Web Resources. See the Output Window for additional details.");
                return true;
            }

            _logger.WriteToOutputWindow("Retrieved Web Resources From CRM", Logger.MessageType.Info);

            List<WebResourceItem> wrItems = new List<WebResourceItem>();
            ObservableCollection<MenuItem> projectFolders = GetProjectFolders(projectName);
            foreach (var entity in results.Entities)
            {
                WebResourceItem wrItem = new WebResourceItem
                {
                    Publish = false,
                    WebResourceId = (Guid)entity.GetAttributeValue<AliasedValue>("webresource.webresourceid").Value,
                    Name = entity.GetAttributeValue<AliasedValue>("webresource.name").Value.ToString(),
                    IsManaged = (bool)entity.GetAttributeValue<AliasedValue>("webresource.ismanaged").Value,
                    AllowPublish = false,
                    AllowCompare = false,
                    TypeName = GetWebResourceTypeNameByNumber(((OptionSetValue)entity.GetAttributeValue<AliasedValue>("webresource.webresourcetype").Value).Value.ToString()),
                    Type = ((OptionSetValue)entity.GetAttributeValue<AliasedValue>("webresource.webresourcetype").Value).Value,
                    ProjectFolders = projectFolders,
                    SolutionId = entity.GetAttributeValue<EntityReference>("solutionid").Id
                };
                object displayName;
                bool hasDisplayName = entity.Attributes.TryGetValue("webresource.displayname", out displayName);
                if (hasDisplayName)
                    wrItem.DisplayName = entity.GetAttributeValue<AliasedValue>("webresource.displayname").Value.ToString();

                wrItem.PropertyChanged += WebResourceItem_PropertyChanged;
                wrItems.Add(wrItem);
            }

            wrItems = wrItems.OrderBy(w => w.Name).ToList();

            wrItems = HandleMappings(wrItems);
            WebResourceGrid.ItemsSource = wrItems;
            FilterWebResources();
            WebResourceGrid.IsEnabled = true;
            WebResourceType.IsEnabled = true;
            ShowManaged.IsEnabled = true;
            AddWebResource.IsEnabled = true;

            _dte.StatusBar.Clear();
            _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
            LockOverlay.Visibility = Visibility.Hidden;

            return true;
        }

        private EntityCollection RetrieveWebResourcesFromCrm(CrmConnection connection)
        {
            try
            {
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    QueryExpression query = new QueryExpression
                    {
                        EntityName = "solutioncomponent",
                        ColumnSet = new ColumnSet("solutionid"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression
                                {
                                    AttributeName = "componenttype",
                                    Operator = ConditionOperator.Equal,
                                    Values = { 61 }
                                }
                            }
                        },
                        LinkEntities =
                        {
                            new LinkEntity
                            {
                                Columns = new ColumnSet("name", "displayname", "webresourcetype", "ismanaged", "webresourceid"),
                                EntityAlias = "webresource",
                                LinkFromEntityName = "solutioncomponent",
                                LinkFromAttributeName = "objectid",
                                LinkToEntityName = "webresource",
                                LinkToAttributeName = "webresourceid",
                                LinkCriteria = new FilterExpression
                                {
                                    Conditions =
                                    {
                                        new ConditionExpression
                                        {
                                            AttributeName = "iscustomizable",
                                            Operator = ConditionOperator.Equal,
                                            Values = { true }
                                        }
                                    }
                                }
                            }
                        }
                    };

                    return orgService.RetrieveMultiple(query);
                }
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow("Error Retrieving Web Resources From CRM: " + crmEx.Message + Environment.NewLine + crmEx.StackTrace, Logger.MessageType.Error);
                return null;
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Retrieving Web Resources From CRM: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
                return null;
            }
        }

        private string GetWebResourceTypeNameByNumber(string type)
        {
            switch (type)
            {
                case "1":
                    return "HTML";
                case "2":
                    return "CSS";
                case "3":
                    return "JS";
                case "4":
                    return "XML";
                case "5":
                    return "PNG";
                case "6":
                    return "JPG";
                case "7":
                    return "GIF";
                case "8":
                    return "XAP";
                case "9":
                    return "XSL";
                case "10":
                    return "ICO";
            }

            return String.Empty;
        }

        private List<WebResourceItem> HandleMappings(List<WebResourceItem> wrItems)
        {
            try
            {
                string projectName = _selectedProject.Name;
                Project project = GetProjectByName(projectName);
                if (project == null)
                    return new List<WebResourceItem>();

                var path = Path.GetDirectoryName(project.FullName);
                if (!ConfigFileExists(project))
                {
                    _logger.WriteToOutputWindow("Error Updating Mappings In Config File: Missing CRMDeveloperExtensions.config File", Logger.MessageType.Error);
                    return wrItems;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(path + "\\CRMDeveloperExtensions.config");

                var props = _dte.Properties["CRM Developer Extensions", "Settings"];
                bool allowPublish = (bool)props.Item("AllowPublishManagedWebResources").Value;

                XmlNodeList mappedFiles = doc.GetElementsByTagName("File");
                List<XmlNode> nodesToRemove = new List<XmlNode>();

                foreach (WebResourceItem wrItem in wrItems)
                {
                    foreach (XmlNode file in mappedFiles)
                    {
                        XmlNode orgIdNode = file["OrgId"];
                        if (orgIdNode == null) continue;
                        if (orgIdNode.InnerText.ToUpper() != _selectedConn.OrgId.ToUpper()) continue;

                        XmlNode webResourceId = file["WebResourceId"];
                        if (webResourceId == null) continue;
                        if (webResourceId.InnerText.ToUpper() != wrItem.WebResourceId.ToString().ToUpper()) continue;

                        XmlNode filePartialPath = file["Path"];
                        if (filePartialPath == null) continue;

                        string filePath = Path.GetDirectoryName(project.FullName) +
                                          filePartialPath.InnerText.Replace("/", "\\");
                        if (!File.Exists(filePath))
                            //Remove mappings for files that might have been deleted from the project
                            nodesToRemove.Add(file);
                        else
                        {
                            wrItem.BoundFile = filePartialPath.InnerText;
                            wrItem.AllowPublish = allowPublish || !wrItem.IsManaged;
                            wrItem.AllowCompare = SetAllowCompare(wrItem.Type);
                        }
                    }
                }

                //Remove mappings for files that might have been deleted from CRM
                mappedFiles = doc.GetElementsByTagName("File");
                foreach (XmlNode file in mappedFiles)
                {
                    XmlNode orgIdNode = file["OrgId"];
                    if (orgIdNode == null) continue;
                    if (orgIdNode.InnerText.ToUpper() != _selectedConn.OrgId.ToUpper()) continue;

                    XmlNode webResourceId = file["WebResourceId"];
                    if (webResourceId == null) continue;

                    var count = wrItems.Count(w => w.WebResourceId.ToString().ToUpper() == webResourceId.InnerText.ToUpper());
                    if (count == 0)
                        nodesToRemove.Add(file);
                }

                //Remove the invalid mappings
                if (nodesToRemove.Count <= 0)
                    return wrItems;

                XmlNode files = nodesToRemove[0].ParentNode;
                foreach (XmlNode xmlNode in nodesToRemove)
                {
                    if (files != null && files.ParentNode != null)
                        files.RemoveChild(xmlNode);
                }
                doc.Save(path + "\\CRMDeveloperExtensions.config");
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Updating Mappings In Config File: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
            }

            return wrItems;
        }

        private bool SetAllowCompare(int type)
        {
            int[] noCompare = { 5, 6, 7, 8, 10 };
            if (!noCompare.Contains(type))
                return true;

            return false;
        }

        private void WebResourceItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            WebResourceItem item = (WebResourceItem)sender;

            if (e.PropertyName == "BoundFile")
            {
                if (WebResourceGrid.ItemsSource != null)
                {
                    List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
                    foreach (WebResourceItem webResourceItem in webResources.Where(w => w.WebResourceId == item.WebResourceId))
                    {
                        webResourceItem.BoundFile = item.BoundFile;
                    }
                }

                AddOrUpdateMapping(item);
            }

            if (e.PropertyName == "Publish")
            {
                List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
                if (webResources == null) return;

                foreach (WebResourceItem webResourceItem in webResources.Where(w => w.WebResourceId == item.WebResourceId))
                {
                    webResourceItem.Publish = item.Publish;
                }

                Publish.IsEnabled = webResources.Count(w => w.Publish) > 0;

                SetPublishAll();
            }
        }

        private void SetPublishAll()
        {
            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            if (webResources == null) return;

            //Set Publish All
            CheckBox publishAll = FindVisualChildren<CheckBox>(WebResourceGrid).FirstOrDefault(t => t.Name == "PublishSelectAll");
            if (publishAll == null) return;

            publishAll.IsChecked = webResources.Count(w => w.Publish) == webResources.Count(w => w.AllowPublish);
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T)
                {
                    yield return (T)child;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private void AddOrUpdateMapping(WebResourceItem item)
        {
            try
            {
                var projectPath = Path.GetDirectoryName(_selectedProject.FullName);
                if (!ConfigFileExists(_selectedProject))
                {
                    _logger.WriteToOutputWindow("Error Updating Mappings In Config File: Missing CRMDeveloperExtensions.config File", Logger.MessageType.Error);
                    return;
                }

                var props = _dte.Properties["CRM Developer Extensions", "Settings"];
                bool allowPublish = (bool)props.Item("AllowPublishManagedWebResources").Value;

                XmlDocument doc = new XmlDocument();
                doc.Load(projectPath + "\\CRMDeveloperExtensions.config");

                //Update or delete existing mapping
                XmlNodeList fileNodes = doc.GetElementsByTagName("File");
                if (fileNodes.Count > 0)
                {
                    foreach (XmlNode node in fileNodes)
                    {
                        XmlNode orgId = node["OrgId"];
                        if (orgId != null && orgId.InnerText.ToUpper() != _selectedConn.OrgId.ToUpper()) continue;

                        XmlNode webResourceId = node["WebResourceId"];
                        if (webResourceId != null && webResourceId.InnerText.ToUpper() !=
                            item.WebResourceId.ToString()
                                .ToUpper()
                                .Replace("{", String.Empty)
                                .Replace("}", String.Empty))
                            continue;

                        if (string.IsNullOrEmpty(item.BoundFile))
                        {
                            //Delete
                            var parentNode = node.ParentNode;
                            if (parentNode != null)
                            {
                                parentNode.RemoveChild(node);

                                item.Publish = false;
                                item.AllowPublish = false;
                                item.AllowCompare = false;
                            }
                        }
                        else
                        {
                            //Update
                            XmlNode path = node["Path"];
                            if (path != null)
                            {
                                path.InnerText = item.BoundFile;
                                int[] noCompare = { 5, 6, 7, 8, 10 };
                                if (!noCompare.Contains(item.Type))
                                    item.AllowCompare = true;

                                item.AllowPublish = allowPublish || !item.IsManaged;
                            }
                        }

                        doc.Save(projectPath + "\\CRMDeveloperExtensions.config");
                        return;
                    }
                }

                if (string.IsNullOrEmpty(item.BoundFile))
                {
                    item.AllowPublish = false;
                    item.Publish = false;
                    return;
                }

                //Create new mapping
                XmlNodeList files = doc.GetElementsByTagName("Files");
                if (files.Count > 0)
                {
                    XmlNode file = doc.CreateElement("File");
                    XmlNode org = doc.CreateElement("OrgId");
                    org.InnerText = _selectedConn.OrgId;
                    file.AppendChild(org);
                    XmlNode path = doc.CreateElement("Path");
                    path.InnerText = item.BoundFile;
                    file.AppendChild(path);
                    XmlNode webResourceId = doc.CreateElement("WebResourceId");
                    webResourceId.InnerText = item.WebResourceId.ToString();
                    file.AppendChild(webResourceId);
                    XmlNode webResourceName = doc.CreateElement("WebResourceName");
                    webResourceName.InnerText = item.Name;
                    file.AppendChild(webResourceName);
                    XmlNode isManaged = doc.CreateElement("IsManaged");
                    isManaged.InnerText = item.IsManaged.ToString();
                    file.AppendChild(isManaged);
                    files[0].AppendChild(file);

                    doc.Save(projectPath + "\\CRMDeveloperExtensions.config");

                    item.AllowPublish = allowPublish || !item.IsManaged;
                    int[] noCompare = { 5, 6, 7, 8, 10 };
                    if (!noCompare.Contains(item.Type))
                        item.AllowCompare = true;
                }
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Updating Mappings In Config File: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
            }
        }

        private void GetWebResource_OnClick(object sender, RoutedEventArgs e)
        {
            Guid webResourceId = new Guid(((Button)sender).CommandParameter.ToString());
            WebResourceItem webResourceItem =
                ((List<WebResourceItem>)WebResourceGrid.ItemsSource)
                    .FirstOrDefault(w => w.WebResourceId == webResourceId);

            string folder = String.Empty;
            if (webResourceItem != null && !string.IsNullOrEmpty(webResourceItem.BoundFile))
            {
                var directoryName = Path.GetDirectoryName(webResourceItem.BoundFile);
                if (directoryName != null)
                    folder = directoryName.Replace("\\", "/");
                if (folder == "/")
                    folder = String.Empty;
            }

            string connString = _selectedConn.ConnectionString;
            string projectName = ((ComboBoxItem)Projects.SelectedItem).Content.ToString();
            DownloadWebResource(webResourceId, folder, connString, projectName);
        }

        private void DownloadWebResourceToFolder(object sender, RoutedEventArgs routedEventArgs)
        {
            MenuItem item = (MenuItem)sender;
            string folder = item.Header.ToString();
            Guid webResourceId = (Guid)item.CommandParameter;

            string connString = _selectedConn.ConnectionString;
            string projectName = _selectedProject.Name;
            DownloadWebResource(webResourceId, folder, connString, projectName);
        }

        private void DownloadWebResource(Guid webResourceId, string folder, string connString, string projectName)
        {
            _dte.StatusBar.Text = "Downloading file...";
            _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);

            try
            {
                CrmConnection connection = CrmConnection.Parse(connString);
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    Entity webResource = orgService.Retrieve("webresource", webResourceId,
                        new ColumnSet("content", "name", "webresourcetype"));

                    _logger.WriteToOutputWindow("Downloaded Web Resource: " + webResource.Id, Logger.MessageType.Info);

                    Project project = GetProjectByName(projectName);
                    string[] name = webResource.GetAttributeValue<string>("name").Split('/');
                    folder = folder.Replace("/", "\\");
                    var path = Path.GetDirectoryName(project.FullName) +
                               ((folder != "\\") ? folder : String.Empty) +
                               "\\" + name[name.Length - 1];

                    //Add missing extension
                    if (string.IsNullOrEmpty(Path.GetExtension(path)))
                    {
                        string ext =
                            GetWebResourceTypeNameByNumber(
                                webResource.GetAttributeValue<OptionSetValue>("webresourcetype").Value.ToString())
                                .ToLower();
                        path += "." + ext;
                    }

                    if (File.Exists(path))
                    {
                        MessageBoxResult result = MessageBox.Show("OK to overwrite?", "Web Resource Download",
                            MessageBoxButton.YesNo);
                        if (result != MessageBoxResult.Yes)
                        {
                            _dte.StatusBar.Clear();
                            return;
                        }
                    }

                    File.WriteAllBytes(path, DecodeWebResource(webResource.GetAttributeValue<string>("content")));

                    ProjectItem projectItem = project.ProjectItems.AddFromFile(path);

                    var fullname = projectItem.FileNames[1];
                    var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
                    if (projectPath == null) return;

                    var boundName = fullname.Replace(projectPath, String.Empty).Replace("\\", "/");

                    List<WebResourceItem> items = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
                    foreach (WebResourceItem item in items.Where(w => w.WebResourceId == webResourceId))
                    {
                        item.BoundFile = boundName;

                        CheckBox publishAll =
                            FindVisualChildren<CheckBox>(WebResourceGrid)
                                .FirstOrDefault(t => t.Name == "PublishSelectAll");
                        if (publishAll == null) return;

                        if (publishAll.IsChecked == true)
                            item.Publish = true;
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow(
                    "Error Downloading Web Resource From CRM: " + crmEx.Message + Environment.NewLine + crmEx.StackTrace,
                    Logger.MessageType.Error);
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow(
                    "Error Downloading Web Resource From CRM: " + ex.Message + Environment.NewLine + ex.StackTrace,
                    Logger.MessageType.Error);
            }
            finally
            {
                _dte.StatusBar.Clear();
                _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
            }
        }

        private void Publish_Click(object sender, RoutedEventArgs e)
        {
            List<WebResourceItem> items = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            List<WebResourceItem> selectedItems = new List<WebResourceItem>();

            //Check for unsaved files
            List<ProjectItem> dirtyItems = new List<ProjectItem>();
            foreach (var selectedItem in items.Where(w => w.Publish))
            {
                selectedItems.Add(selectedItem);

                ObservableCollection<ComboBoxItem> projectFiles =
                    (ObservableCollection<ComboBoxItem>)ProjectFileList.ItemsSource;
                ComboBoxItem item = projectFiles.FirstOrDefault(c => c.Content.ToString() == selectedItem.BoundFile);

                if (item == null) continue;

                ProjectItem projectItem = (ProjectItem)item.Tag;
                if (projectItem.IsDirty)
                    dirtyItems.Add(projectItem);
            }

            if (dirtyItems.Count > 0)
            {
                var result = MessageBox.Show("Save item(s) and publish?", "Unsaved Item(s)", MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes) return;

                foreach (var projectItem in dirtyItems)
                {
                    projectItem.Save();
                }
            }

            //Build TypeScript project
            if (selectedItems.Any(p => p.BoundFile.ToUpper().EndsWith("TS")))
            {
                SolutionBuild solutionBuild = _dte.Solution.SolutionBuild;
                solutionBuild.BuildProject(_dte.Solution.SolutionBuild.ActiveConfiguration.Name, _selectedProject.UniqueName, true);
            }

            UpdateWebResources(selectedItems);
        }

        private async void UpdateWebResources(List<WebResourceItem> items)
        {
            string projectName = _selectedProject.Name;
            Project project = GetProjectByName(projectName);
            if (project == null) return;

            string connString = _selectedConn.ConnectionString;
            if (connString == null) return;
            CrmConnection connection = CrmConnection.Parse(connString);

            LockMessage.Content = "Publishing...";
            LockOverlay.Visibility = Visibility.Visible;

            bool success;
            //Check if < CRM 2011 UR12 (ExecuteMutliple)
            Version version = Version.Parse(_selectedConn.Version);
            if (version.Major == 5 && version.Revision < 3200)
                success = await System.Threading.Tasks.Task.Run(() => UpdateAndPublishSingle(items, project, connection));
            else
                success = await System.Threading.Tasks.Task.Run(() => UpdateAndPublishMultiple(items, project, connection));

            LockOverlay.Visibility = Visibility.Hidden;

            if (success) return;

            MessageBox.Show("Error Updating And Publishing Web Resources. See the Output Window for additional details.");
            _dte.StatusBar.Clear();
        }

        private bool UpdateAndPublishMultiple(List<WebResourceItem> items, Project project, CrmConnection connection)
        {
            //CRM 2011 UR12+
            try
            {
                ExecuteMultipleRequest emRequest = new ExecuteMultipleRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = false,
                        ReturnResponses = true
                    }
                };

                OrganizationRequestCollection requests = new OrganizationRequestCollection();

                string publishXml = "<importexportxml><webresources>";
                foreach (var webResourceItem in items)
                {
                    Entity webResource = new Entity("webresource") { Id = webResourceItem.WebResourceId };

                    string filePath = Path.GetDirectoryName(project.FullName) +
                                      webResourceItem.BoundFile.Replace("/", "\\");
                    if (!File.Exists(filePath)) continue;

                    string extension = Path.GetExtension(filePath);

                    List<string> imageExs = new List<string>() { ".ICO", ".PNG", ".GIF", ".JPG" };
                    string content;
                    //TypeScript
                    if ((extension.ToUpper() == ".TS"))
                    {
                        content = File.ReadAllText(Path.ChangeExtension(filePath, ".js"));
                        webResource["content"] = EncodeString(content);
                    }
                    //Images
                    else if (imageExs.Any(s => extension.ToUpper().EndsWith(s)))
                    {
                        content = EncodedImage(filePath, extension);
                        webResource["content"] = content;
                    }
                    //Everything else
                    else
                    {
                        content = File.ReadAllText(filePath);
                        webResource["content"] = EncodeString(content);
                    }

                    UpdateRequest request = new UpdateRequest { Target = webResource };
                    requests.Add(request);

                    publishXml += "<webresource>{" + webResource.Id + "}</webresource>";
                }
                publishXml += "</webresources></importexportxml>";

                PublishXmlRequest pubRequest = new PublishXmlRequest { ParameterXml = publishXml };
                requests.Add(pubRequest);
                emRequest.Requests = requests;

                bool wasError = false;
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    _dte.StatusBar.Text = "Updating & publishing web resource(s)...";
                    _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationDeploy);

                    ExecuteMultipleResponse emResponse = (ExecuteMultipleResponse)orgService.Execute(emRequest);

                    foreach (var responseItem in emResponse.Responses)
                    {
                        if (responseItem.Fault == null) continue;

                        _logger.WriteToOutputWindow(
                            "Error Updating And Publishing Web Resource(s) To CRM: " + responseItem.Fault.Message +
                            Environment.NewLine + responseItem.Fault.TraceText, Logger.MessageType.Error);
                        wasError = true;
                    }

                    if (wasError)
                    {
                        MessageBox.Show(
                            "Error Updating And Publishing Web Resource(s) To CRM. See the Output Window for additional details.");
                        _dte.StatusBar.Clear();
                        return false;
                    }
                }

                _logger.WriteToOutputWindow("Updated And Published Web Resource(s)", Logger.MessageType.Info);

                return true;
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow("Error Updating And Publishing Web Resource(s) To CRM: " +
                    crmEx.Message + Environment.NewLine + crmEx.StackTrace, Logger.MessageType.Error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Updating And Publishing Web Resource(s) To CRM: " +
                    ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
                return false;
            }
            finally
            {
                _dte.StatusBar.Clear();
                _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationDeploy);
            }
        }

        private bool UpdateAndPublishSingle(List<WebResourceItem> items, Project project, CrmConnection connection)
        {
            //CRM 2011 < UR12
            _dte.StatusBar.Text = "Updating & publishing web resource(s)...";
            _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationDeploy);

            try
            {
                string publishXml = "<importexportxml><webresources>";
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    foreach (var webResourceItem in items)
                    {
                        Entity webResource = new Entity("webresource") { Id = webResourceItem.WebResourceId };

                        string filePath = Path.GetDirectoryName(project.FullName) +
                                          webResourceItem.BoundFile.Replace("/", "\\");
                        if (!File.Exists(filePath)) continue;

                        string extension = Path.GetExtension(filePath);

                        List<string> imageExs = new List<string>() { ".ICO", ".PNG", ".GIF", ".JPG" };
                        string content;
                        //TypeScript
                        if ((extension.ToUpper() == ".TS"))
                        {
                            content = File.ReadAllText(Path.ChangeExtension(filePath, ".js"));
                            webResource["content"] = EncodeString(content);
                        }
                        //Images
                        else if (imageExs.Any(s => extension.ToUpper().EndsWith(s)))
                        {
                            content = EncodedImage(filePath, extension);
                            webResource["content"] = content;
                        }
                        //Everything else
                        else
                        {
                            content = File.ReadAllText(filePath);
                            webResource["content"] = EncodeString(content);
                        }

                        UpdateRequest request = new UpdateRequest { Target = webResource };
                        orgService.Execute(request);
                        _logger.WriteToOutputWindow("Uploaded Web Resource", Logger.MessageType.Info);

                        publishXml += "<webresource>{" + webResource.Id + "}</webresource>";
                    }
                    publishXml += "</webresources></importexportxml>";

                    PublishXmlRequest pubRequest = new PublishXmlRequest { ParameterXml = publishXml };

                    orgService.Execute(pubRequest);
                    _logger.WriteToOutputWindow("Published Web Resource(s)", Logger.MessageType.Info);
                }

                return true;
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow("Error Updating And Publishing Web Resource(s) To CRM: " + crmEx.Message + Environment.NewLine +
                    crmEx.StackTrace, Logger.MessageType.Error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Updating And Publishing Web Resource(s) To CRM: " + ex.Message + Environment.NewLine +
                    ex.StackTrace, Logger.MessageType.Error);
                return false;
            }
            finally
            {
                _dte.StatusBar.Clear();
                _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationDeploy);
            }
        }

        private void Connections_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConn = (CrmConn)Connections.SelectedItem;
            if (_selectedConn != null)
            {
                Connect.IsEnabled = !string.IsNullOrEmpty(_selectedConn.Name);
                Delete.IsEnabled = !string.IsNullOrEmpty(_selectedConn.Name);
                ModifyConnection.IsEnabled = !string.IsNullOrEmpty(_selectedConn.Name);

                if (_connectionAdded)
                {
                    _connectionAdded = false;
                    Customizations.IsEnabled = true;
                    Solutions.IsEnabled = true;
                    SolutionList.IsEnabled = true;
                    AddWebResource.IsEnabled = true;
                }
                else
                {
                    UpdateSelectedConnection(false);
                    Customizations.IsEnabled = false;
                    Solutions.IsEnabled = false;
                    SolutionList.IsEnabled = false;
                    AddWebResource.IsEnabled = false;
                }
            }
            else
            {
                Connect.IsEnabled = false;
                Delete.IsEnabled = false;
                ModifyConnection.IsEnabled = false;
                Customizations.IsEnabled = false;
                Solutions.IsEnabled = false;
                SolutionList.IsEnabled = false;
                AddWebResource.IsEnabled = false;
            }

            WebResourceType.SelectedIndex = -1;
            WebResourceGrid.ItemsSource = null;
            WebResourceType.IsEnabled = false;
            ShowManaged.IsEnabled = false;
            Publish.IsEnabled = false;
            WebResourceGrid.IsEnabled = false;
        }

        private void UpdateSelectedConnection(bool makeSelected)
        {
            try
            {
                var path = Path.GetDirectoryName(_selectedProject.FullName);
                if (!ConfigFileExists(_selectedProject))
                {
                    _logger.WriteToOutputWindow("Error Updating Connection: Missing CRMDeveloperExtensions.config File", Logger.MessageType.Error);
                    return;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(path + "\\CRMDeveloperExtensions.config");

                XmlNodeList connections = doc.GetElementsByTagName("Connection");
                if (connections.Count > 0)
                {
                    foreach (XmlNode node in connections)
                    {
                        XmlNode name = node["Name"];
                        if (name == null) continue;

                        XmlNode selected = node["Selected"];
                        if (selected == null) continue;

                        if (makeSelected)
                            selected.InnerText = name.InnerText != _selectedConn.Name ? "False" : "True";
                        else
                            selected.InnerText = "False";
                    }

                    doc.Save(path + "\\CRMDeveloperExtensions.config");
                }
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Updating Connection: Missing CRMDeveloperExtensions.config File: " + ex.Message, Logger.MessageType.Error);
            }
        }

        private void WebResourceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterWebResources();
        }

        private void Projects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //No solution loaded
            if (_solution.Count == 0) return;

            WebResourceGrid.ItemsSource = null;
            WebResourceType.IsEnabled = false;
            ShowManaged.IsEnabled = false;
            Publish.IsEnabled = false;
            Customizations.IsEnabled = false;
            Solutions.IsEnabled = false;
            SolutionList.IsEnabled = false;
            AddWebResource.IsEnabled = false;
            WebResourceGrid.IsEnabled = false;

            ComboBoxItem item = (ComboBoxItem)Projects.SelectedItem;
            if (item == null) return;
            if (string.IsNullOrEmpty(item.Content.ToString())) return;

            _selectedProject = (Project)((ComboBoxItem)Projects.SelectedItem).Tag;
            ProjectFileList.ItemsSource = GetProjectFiles(_selectedProject.Name);
            GetConnections();
        }

        private void ModifyConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConn == null) return;
            if (string.IsNullOrEmpty(_selectedConn.ConnectionString)) return;

            string name = _selectedConn.Name;
            Connection connection = new Connection(name, _selectedConn.ConnectionString);
            bool? result = connection.ShowDialog();

            if (!result.HasValue || !result.Value) return;

            var configExists = ConfigFileExists(_selectedProject);
            if (!configExists)
                CreateConfigFile(_selectedProject);

            Expander.IsExpanded = false;

            AddOrUpdateConnection(_selectedProject, connection.ConnectionName, connection.ConnectionString, connection.OrgId, connection.Version, false);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBoxResult result = MessageBox.Show("Are you sure?" + Environment.NewLine + Environment.NewLine +
                    "This will delete the connection information and all associated mappings.", "Delete Connection", MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes) return;

                if (_selectedConn == null) return;
                if (string.IsNullOrEmpty(_selectedConn.ConnectionString)) return;

                var path = Path.GetDirectoryName(_selectedProject.FullName);
                if (!ConfigFileExists(_selectedProject))
                {
                    _logger.WriteToOutputWindow("Error Deleting Connection: Missing CRMDeveloperExtensions.config File", Logger.MessageType.Error);
                    return;
                }

                if (!ConfigFileExists(_selectedProject)) return;

                //Delete Connection
                XmlDocument doc = new XmlDocument();
                doc.Load(path + "\\CRMDeveloperExtensions.config");
                XmlNodeList connections = doc.GetElementsByTagName("Connection");
                if (connections.Count == 0) return;

                List<XmlNode> nodesToRemove = new List<XmlNode>();
                foreach (XmlNode connection in connections)
                {
                    XmlNode orgId = connection["OrgId"];
                    if (orgId == null) continue;
                    if (orgId.InnerText.ToUpper() != _selectedConn.OrgId.ToUpper()) continue;

                    nodesToRemove.Add(connection);
                }

                foreach (XmlNode xmlNode in nodesToRemove)
                {
                    if (xmlNode.ParentNode != null)
                        xmlNode.ParentNode.RemoveChild(xmlNode);
                }
                doc.Save(path + "\\CRMDeveloperExtensions.config");

                //Delete related Files
                doc.Load(path + "\\CRMDeveloperExtensions.config");
                XmlNodeList files = doc.GetElementsByTagName("File");
                if (files.Count > 0)
                {
                    nodesToRemove = new List<XmlNode>();
                    foreach (XmlNode file in files)
                    {
                        XmlNode orgId = file["OrgId"];
                        if (orgId == null) continue;
                        if (orgId.InnerText.ToUpper() != _selectedConn.OrgId.ToUpper()) continue;

                        nodesToRemove.Add(file);
                    }

                    foreach (XmlNode xmlNode in nodesToRemove)
                    {
                        if (xmlNode.ParentNode != null)
                            xmlNode.ParentNode.RemoveChild(xmlNode);
                    }
                    doc.Save(path + "\\CRMDeveloperExtensions.config");
                }

                WebResourceGrid.ItemsSource = null;
                WebResourceType.IsEnabled = false;
                ShowManaged.IsEnabled = false;
                Publish.IsEnabled = false;
                Customizations.IsEnabled = false;
                Solutions.IsEnabled = false;
                SolutionList.IsEnabled = false;
                AddWebResource.IsEnabled = false;
                WebResourceGrid.IsEnabled = false;

                GetConnections();
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow("Error Deleting Connection: Missing CRMDeveloperExtensions.config File: " + ex.Message + Environment.NewLine + ex.StackTrace, Logger.MessageType.Error);
            }
        }

        private void OpenWebResource_OnClick(object sender, RoutedEventArgs e)
        {
            Guid webResourceId = new Guid(((Button)sender).CommandParameter.ToString());

            OpenCrmPage("main.aspx?etc=9333&id=%7b" + webResourceId + "%7d&pagetype=webresourceedit");
        }

        private void ShowManaged_Checked(object sender, RoutedEventArgs e)
        {
            FilterWebResources();
        }

        private void FilterWebResources()
        {
            ComboBoxItem selectedItem = (ComboBoxItem)WebResourceType.SelectedItem;
            string type = (selectedItem == null) ? String.Empty : selectedItem.Tag.ToString();
            bool showManaged = ShowManaged.IsChecked != null && ShowManaged.IsChecked.Value;
            Guid solutionId = (SolutionList.SelectedItem != null) ?
                ((CrmSolution)SolutionList.SelectedItem).SolutionId :
                new Guid("FD140AAF-4DF4-11DD-BD17-0019B9312238"); //Default Solution


            //Clear publish flags
            if (!string.IsNullOrEmpty(type))
            {
                List<WebResourceItem> items = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
                foreach (WebResourceItem webResourceItem in items)
                {
                    if (selectedItem != null && (webResourceItem.Type.ToString() != selectedItem.Tag.ToString() || (webResourceItem.IsManaged && !showManaged)))
                        webResourceItem.Publish = false;
                }
            }

            //Filter the items
            ICollectionView icv = CollectionViewSource.GetDefaultView(WebResourceGrid.ItemsSource);
            if (icv == null) return;

            icv.Filter = o =>
            {
                WebResourceItem w = o as WebResourceItem;
                //File type filter & show managed + unmanaged
                if (!string.IsNullOrEmpty(type) && showManaged)
                    return w != null && (w.Type.ToString() == type && w.SolutionId == solutionId);

                //File type filter & show unmanaged only
                if (!string.IsNullOrEmpty(type) && !showManaged)
                    return w != null && (w.Type.ToString() == type && !w.IsManaged && w.SolutionId == solutionId);

                //No file type filter & show managed + unmanaged
                if (string.IsNullOrEmpty(type) && showManaged)
                    return w != null && (w.SolutionId == solutionId);

                //No file type filter & show unmanaged only
                return w != null && (!w.IsManaged && w.SolutionId == solutionId);
            };

            //Item Count
            CollectionView cv = (CollectionView)icv;
            ItemCount.Text = cv.Count + " Items";
        }

        private void CompareWebResource_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedConn == null) return;
                string connString = _selectedConn.ConnectionString;
                if (string.IsNullOrEmpty(connString)) return;

                CrmConnection connection = CrmConnection.Parse(connString);
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    _dte.StatusBar.Text = "Downloading file for compare...";
                    _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);

                    //Get the file from CRM and save in temp files
                    Guid webResourceId = new Guid(((Button)sender).CommandParameter.ToString());
                    Entity webResource = orgService.Retrieve("webresource", webResourceId,
                        new ColumnSet("content", "name"));

                    _logger.WriteToOutputWindow("Retrieved Web Resource " + webResourceId + " For Compare",
                        Logger.MessageType.Info);

                    var tempFolder = Path.GetTempPath();
                    string fileName = Path.GetFileName(webResource.GetAttributeValue<string>("name"));
                    if (string.IsNullOrEmpty(fileName))
                        fileName = Guid.NewGuid().ToString();
                    var tempFile = Path.Combine(tempFolder, fileName);
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                    File.WriteAllBytes(tempFile, DecodeWebResource(webResource.GetAttributeValue<string>("content")));

                    //Get the corresponding project item 
                    string projectName = _selectedProject.Name;
                    Project project = GetProjectByName(projectName);
                    var projectPath = Path.GetDirectoryName(project.FullName);
                    if (projectPath == null) return;

                    string boundFilePath = String.Empty;
                    List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
                    foreach (WebResourceItem webResourceItem in webResources)
                    {
                        if (webResourceItem.WebResourceId == webResourceId)
                            boundFilePath = webResourceItem.BoundFile;
                    }

                    _dte.ExecuteCommand("Tools.DiffFiles",
                        string.Format("\"{0}\" \"{1}\" \"{2}\" \"{3}\"", tempFile,
                            projectPath + boundFilePath.Replace("/", "\\"),
                            webResource.GetAttributeValue<string>("name") + " - CRM", boundFilePath + " - Local"));
                }
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow(
                    "Error Performing Compare Operation: " + crmEx.Message + Environment.NewLine + crmEx.StackTrace,
                    Logger.MessageType.Error);
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow(
                    "Error Performing Compare Operation: " + ex.Message + Environment.NewLine + ex.StackTrace,
                    Logger.MessageType.Error);
            }
            finally
            {
                _dte.StatusBar.Clear();
                _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
            }
        }

        private void WebResourceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Make rows unselectable
            WebResourceGrid.UnselectAllCells();
        }

        private void AddWebResource_Click(object sender, RoutedEventArgs e)
        {
            NewWebResource newWebResource = new NewWebResource(_selectedConn, _selectedProject, GetProjectFiles(_selectedProject.Name));
            bool? result = newWebResource.ShowDialog();

            if (result != true) return;

            //Add new item
            WebResourceItem wrItem = new WebResourceItem
            {
                Publish = false,
                WebResourceId = newWebResource.NewId,
                Name = newWebResource.NewName,
                DisplayName = newWebResource.NewDisplayName,
                IsManaged = false,
                AllowPublish = true,
                AllowCompare = SetAllowCompare(newWebResource.NewType),
                TypeName = GetWebResourceTypeNameByNumber(newWebResource.NewType.ToString()),
                Type = newWebResource.NewType,
                ProjectFolders = GetProjectFolders(_selectedProject.Name),
                SolutionId = newWebResource.NewSolutionId
            };

            wrItem.PropertyChanged += WebResourceItem_PropertyChanged;
            //Needs to be after setting the property changed event
            wrItem.BoundFile = newWebResource.NewBoundFile;

            foreach (MenuItem menuItem in wrItem.ProjectFolders)
            {
                menuItem.CommandParameter = wrItem.WebResourceId;
            }

            List<WebResourceItem> items = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            items.Add(wrItem);

            //Add an instance assigned to the default solution if required
            if (wrItem.SolutionId != new Guid("FD140AAF-4DF4-11DD-BD17-0019B9312238"))
            {
                WebResourceItem wrItem2 = new WebResourceItem
                {
                    Publish = false,
                    WebResourceId = newWebResource.NewId,
                    Name = newWebResource.NewName,
                    DisplayName = newWebResource.NewDisplayName,
                    IsManaged = false,
                    AllowPublish = true,
                    AllowCompare = SetAllowCompare(newWebResource.NewType),
                    TypeName = GetWebResourceTypeNameByNumber(newWebResource.NewType.ToString()),
                    Type = newWebResource.NewType,
                    ProjectFolders = GetProjectFolders(_selectedProject.Name),
                    SolutionId = new Guid("FD140AAF-4DF4-11DD-BD17-0019B9312238")
                };

                wrItem2.PropertyChanged += WebResourceItem_PropertyChanged;
                //Needs to be after setting the property changed event
                wrItem2.BoundFile = newWebResource.NewBoundFile;

                foreach (MenuItem menuItem in wrItem2.ProjectFolders)
                {
                    menuItem.CommandParameter = wrItem2.WebResourceId;
                }

                items.Add(wrItem2);
            }

            WebResourceGrid.ItemsSource = items.OrderBy(w => w.Name).ToList();

            var filter = WebResourceType.SelectedValue;
            var showManaged = ShowManaged.IsChecked;

            FilterWebResources();

            WebResourceType.SelectedValue = filter;
            ShowManaged.IsChecked = showManaged;

            WebResourceGrid.ScrollIntoView(wrItem);
        }

        private void Info_OnClick(object sender, RoutedEventArgs e)
        {
            Info info = new Info();
            info.ShowDialog();
        }

        private void UpdateAllPublishChecks(bool publish)
        {
            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            foreach (WebResourceItem webResourceItem in webResources)
            {
                if (webResourceItem.AllowPublish)
                    webResourceItem.Publish = publish;
            }
        }

        private void PublishSelectAll_OnClick(object sender, RoutedEventArgs e)
        {
            CheckBox publishAll = (CheckBox)sender;
            bool? isChecked = publishAll.IsChecked;

            if (isChecked != null && isChecked.Value)
                UpdateAllPublishChecks(true);
            else
                UpdateAllPublishChecks(false);
        }

        private void Customizations_OnClick(object sender, RoutedEventArgs e)
        {
            OpenCrmPage("tools/solution/edit.aspx?id=%7bfd140aaf-4df4-11dd-bd17-0019b9312238%7d");
        }

        private void Solutions_OnClick(object sender, RoutedEventArgs e)
        {

            OpenCrmPage("tools/Solution/home_solution.aspx?etc=7100&sitemappath=Settings|Customizations|nav_solution");
        }

        private void OpenCrmPage(string url)
        {
            if (_selectedConn == null) return;
            string connString = _selectedConn.ConnectionString;
            if (string.IsNullOrEmpty(connString)) return;

            string[] connParts = connString.Split(';');
            string urlPart = connParts.FirstOrDefault(s => s.ToUpper().StartsWith("URL="));
            if (!string.IsNullOrEmpty(urlPart))
            {
                string[] urlParts = urlPart.Split('=');
                string baseUrl = (urlParts[1].EndsWith("/")) ? urlParts[1] : urlParts[1] + "/";

                var props = _dte.Properties["CRM Developer Extensions", "Settings"];
                bool useDefaultWebBrowser = (bool)props.Item("UseDefaultWebBrowser").Value;

                if (useDefaultWebBrowser) //User's default browser
                    System.Diagnostics.Process.Start(baseUrl + url);
                else //Internal VS browser
                    _dte.ItemOperations.Navigate(baseUrl + url);
            }
        }

        private void SolutionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedConn == null) return;

            string connString = _selectedConn.ConnectionString;
            if (string.IsNullOrEmpty(connString)) return;

            FilterWebResources();
        }

        private async Task<bool> GetSolutions()
        {
            _dte.StatusBar.Text = "Connecting to CRM and getting solutions...";
            _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);
            LockMessage.Content = "Working...";
            LockOverlay.Visibility = Visibility.Visible;

            List<CrmSolution> solutions = new List<CrmSolution>();
            CrmConnection connection = CrmConnection.Parse(_selectedConn.ConnectionString);

            EntityCollection results = await System.Threading.Tasks.Task.Run(() => RetrieveSolutionsFromCrm(connection));
            if (results == null)
            {
                _dte.StatusBar.Clear();
                LockOverlay.Visibility = Visibility.Hidden;
                MessageBox.Show("Error Retrieving Solutions. See the Output Window for additional details.");
                return true;
            }

            _logger.WriteToOutputWindow("Retrieved Solutions From CRM", Logger.MessageType.Info);

            foreach (Entity entity in results.Entities)
            {
                CrmSolution solution = new CrmSolution
                {
                    SolutionId = entity.Id,
                    Name = entity.GetAttributeValue<string>("friendlyname"),
                    Prefix = entity.GetAttributeValue<AliasedValue>("publisher.customizationprefix").Value.ToString(),
                    UniqueName = entity.GetAttributeValue<string>("uniquename")
                };

                solutions.Add(solution);
            }

            //Default on top
            var i = solutions.FindIndex(s => s.SolutionId == new Guid("FD140AAF-4DF4-11DD-BD17-0019B9312238"));
            var item = solutions[i];
            solutions.RemoveAt(i);
            solutions.Insert(0, item);

            SolutionList.ItemsSource = solutions;
            SolutionList.SelectedIndex = 0;

            _dte.StatusBar.Clear();
            _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
            LockOverlay.Visibility = Visibility.Hidden;

            return true;
        }

        private EntityCollection RetrieveSolutionsFromCrm(CrmConnection connection)
        {
            try
            {
                using (OrganizationService orgService = new OrganizationService(connection))
                {
                    QueryExpression query = new QueryExpression
                    {
                        EntityName = "solution",
                        ColumnSet = new ColumnSet("friendlyname", "solutionid", "uniquename"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression
                                {
                                    AttributeName = "isvisible",
                                    Operator = ConditionOperator.Equal,
                                    Values = {true}
                                }
                            }
                        },
                        LinkEntities =
                        {
                            new LinkEntity
                            {
                                LinkFromEntityName = "solution",
                                LinkFromAttributeName = "publisherid",
                                LinkToEntityName = "publisher",
                                LinkToAttributeName = "publisherid",
                                Columns = new ColumnSet("customizationprefix"),
                                EntityAlias = "publisher"
                            },
                            new LinkEntity
						    {
							    LinkFromEntityName = "solution",
							    LinkFromAttributeName = "solutionid",
							    LinkToEntityName = "solutioncomponent",
							    LinkToAttributeName = "solutionid",
							    JoinOperator = JoinOperator.Natural,
							    LinkCriteria =
							    {
								    Conditions =
								    {
									    new ConditionExpression
									    {
										    AttributeName = "componenttype",
										    Operator = ConditionOperator.Equal,
										    Values = { 61 }
									    }
								    }
							    },
                                LinkEntities =
                                {
                                    new LinkEntity
                                    {
                                        LinkFromEntityName = "solutioncomponent",
                                        LinkFromAttributeName = "objectid",
                                        LinkToEntityName = "webresource",
                                        LinkToAttributeName = "webresourceid",
                                        LinkCriteria =
                                        {
                                            Conditions =
                                            {
                                                new ConditionExpression
									            {
										            AttributeName = "iscustomizable",
										            Operator = ConditionOperator.Equal,
										            Values = { true }
									            }
                                            }
                                        }
                                    }
                                }
						    }
                        },
                        Distinct = true,
                        Orders =
                        {
                            new OrderExpression
                            {
                                AttributeName = "friendlyname",
                                OrderType = OrderType.Ascending
                            }
                        }
                    };

                    return orgService.RetrieveMultiple(query);
                }
            }
            catch (FaultException<OrganizationServiceFault> crmEx)
            {
                _logger.WriteToOutputWindow(
                    "Error Retrieving Solutions From CRM: " + crmEx.Message + Environment.NewLine + crmEx.StackTrace,
                    Logger.MessageType.Error);
                return null;
            }
            catch (Exception ex)
            {
                _logger.WriteToOutputWindow(
                    "Error Retrieving Solutions From CRM: " + ex.Message + Environment.NewLine + ex.StackTrace,
                    Logger.MessageType.Error);
                return null;
            }
        }

        private void ProjectFileList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectFileList.SelectedIndex == -1) return;

            WebResourceItem webResourceItem =
                ((List<WebResourceItem>)WebResourceGrid.ItemsSource)
                    .FirstOrDefault(w => w.WebResourceId == new Guid(FileId.Content.ToString()));

            ComboBoxItem item = (ComboBoxItem)ProjectFileList.SelectedItem;

            if (webResourceItem != null && webResourceItem.BoundFile != item.Content.ToString())
                webResourceItem.BoundFile = item.Content.ToString();

            FilePopup.IsOpen = false;
        }

        private void BoundFile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Grid grid = (Grid)sender;
            TextBlock textBlock = (TextBlock)grid.Children[0];

            Guid webResourceId = new Guid(textBlock.Tag.ToString());
            FileId.Content = webResourceId;

            List<WebResourceItem> webResources = (List<WebResourceItem>)WebResourceGrid.ItemsSource;
            WebResourceItem webResourceItem = webResources.FirstOrDefault(w => w.WebResourceId == webResourceId);
            ProjectFileList.SelectedIndex = -1;
            if (webResourceItem != null)
            {
                foreach (ComboBoxItem comboBoxItem in ProjectFileList.Items)
                {
                    if (comboBoxItem.Content.ToString() != webResourceItem.BoundFile) continue;

                    ProjectFileList.SelectedItem = comboBoxItem;
                    break;
                }
            }

            ProjectFileList.Width = WebResourceGrid.Columns[5].ActualWidth - 2;
            FilePopup.PlacementTarget = textBlock;
            FilePopup.Placement = PlacementMode.Relative;
            FilePopup.IsOpen = true;
            ProjectFileList.IsDropDownOpen = true;
        }
    }
}
