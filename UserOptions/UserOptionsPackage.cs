﻿using Microsoft.VisualStudio.Shell;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UserOptions
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [ProvideMenuResource("Menus.ctmenu", 1), Guid(GuidList.GuidUserOptionsPkgString)]
    [ProvideOptionPage(typeof(OptionPageCustom), "CRM Developer Extensions", "Settings", 0, 0, true)]
    public sealed class UserOptionsPackage : Package
    {
        protected override void Initialize()
        {
            base.Initialize();

        }
    }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [Guid("1D9ECCF3-5D2F-4112-9B25-264596873DC9")]
    public class OptionPageCustom : DialogPage
    {
        private string _defaultCrmSdkVersion = "CRM 2015 (7.1.X)";
        private string _defaultProjectKeyFileName = "MyKey";
        private bool _enableCrmSdkSearch = true;

        public string DefaultCrmSdkVersion
        {
            get { return _defaultCrmSdkVersion; }
            set { _defaultCrmSdkVersion = value; }
        }

        public string DefaultProjectKeyFileName
        {
            get { return _defaultProjectKeyFileName; }
            set { _defaultProjectKeyFileName = value; }
        }

        public bool EnableCrmSdkSearch
        {
            get { return _enableCrmSdkSearch; }
            set { _enableCrmSdkSearch = value; }
        }

        public bool AllowPublishManagedWebResources { get; set; }
        public bool AllowPublishManagedReports { get; set; }
        public bool UseDefaultWebBrowser { get; set; }
        public string RegistrationToolPath { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        protected override IWin32Window Window
        {
            get
            {
                OptionsControl page = new OptionsControl
                {
                    DefaultCrmSdkVersion = this,
                    DefaultProjectKeyFileName = this,
                    AllowPublishManagedWebResources = this,
                    AllowPublishManagedReports = this,
                    UseDefaultWebBrowser = this,
                    EnableCrmSdkSearch = this,
                    RegistraionToolPath = this
                };
                page.Initialize();
                return page;
            }
        }
    }
}
