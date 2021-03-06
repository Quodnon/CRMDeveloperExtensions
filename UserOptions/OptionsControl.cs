﻿using System;
using System.IO;
using System.Windows.Forms;

namespace UserOptions
{
    public partial class OptionsControl : UserControl
    {
        public OptionsControl()
        {
            InitializeComponent();
        }

        internal OptionPageCustom DefaultCrmSdkVersion;
        internal OptionPageCustom DefaultProjectKeyFileName;
        internal OptionPageCustom AllowPublishManagedWebResources;
        internal OptionPageCustom AllowPublishManagedReports;
        internal OptionPageCustom UseDefaultWebBrowser;
        internal OptionPageCustom EnableCrmSdkSearch;
        internal OptionPageCustom RegistraionToolPath;

        public void Initialize()
        {
            DefaultSdkVersion.SelectedIndex = DefaultSdkVersion.FindStringExact(!string.IsNullOrEmpty(DefaultCrmSdkVersion.DefaultCrmSdkVersion)
                                                  ? DefaultCrmSdkVersion.DefaultCrmSdkVersion
                                                  : "CRM 2015 (7.1.X)");
            DefaultKeyFileName.Text = DefaultProjectKeyFileName.DefaultProjectKeyFileName;
            AllowPublishManaged.Checked = AllowPublishManagedWebResources.AllowPublishManagedWebResources;
            AllowPublishManagedRpts.Checked = AllowPublishManagedReports.AllowPublishManagedReports;
            DefaultWebBrowser.Checked = UseDefaultWebBrowser.UseDefaultWebBrowser;
            EnableSdkSearch.Checked = EnableCrmSdkSearch.EnableCrmSdkSearch;
            PrtName.Text = RegistraionToolPath.RegistrationToolPath;
        }

        private void DefaultSdkVersion_SelectedIndexChanged(object sender, EventArgs e)
        {
            DefaultCrmSdkVersion.DefaultCrmSdkVersion = DefaultSdkVersion.SelectedItem.ToString();
        }

        private void DefaultKeyFileName_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(DefaultKeyFileName.Text))
            {
                HandleIllegalFileName();
                return;
            }

            string name = DefaultKeyFileName.Text + ".snk";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                HandleIllegalFileName();
                return;
            }

            if (name.StartsWith("."))
            {
                HandleIllegalFileName();
                return;
            }

            if (name.ToUpper() == "CON.SNK" || name.ToUpper() == "AUX.SNK" || name.ToUpper() == "PRN.SNK" || name.ToUpper() == "COM1.SNK" || name.ToUpper() == "LPT2.SNK")
            {
                HandleIllegalFileName();
                return;
            }

            DefaultProjectKeyFileName.DefaultProjectKeyFileName = DefaultKeyFileName.Text.Trim();
        }

        private void HandleIllegalFileName()
        {
            MessageBox.Show("Illegal file name");
            DefaultProjectKeyFileName.DefaultProjectKeyFileName = "MyKey";
            DefaultKeyFileName.Text = "MyKey";
        }

        private void AllowPublishManaged_CheckedChanged(object sender, EventArgs e)
        {
            AllowPublishManagedWebResources.AllowPublishManagedWebResources = AllowPublishManaged.Checked;
        }

        private void DefaultWebBrowser_CheckedChanged(object sender, EventArgs e)
        {
            UseDefaultWebBrowser.UseDefaultWebBrowser = DefaultWebBrowser.Checked;
        }

        private void EnableSdkSearch_CheckedChanged(object sender, EventArgs e)
        {
            EnableCrmSdkSearch.EnableCrmSdkSearch = EnableSdkSearch.Checked;
        }

        private void AllowPublishManagedRpts_CheckedChanged(object sender, EventArgs e)
        {
            AllowPublishManagedReports.AllowPublishManagedReports = AllowPublishManagedRpts.Checked;
        }

        private void PrtName_TextChanged(object sender, EventArgs e)
        {
            string path = PrtName.Text.Trim();

            if (string.IsNullOrEmpty(path))
            {
                RegistraionToolPath.RegistrationToolPath = null;
                return;
            }

            if (path.EndsWith(".exe", StringComparison.CurrentCultureIgnoreCase))
                path = Path.GetDirectoryName(path);

            if (path != null && !path.EndsWith("\\"))
                path += "\\";

            RegistraionToolPath.RegistrationToolPath = path;
        }
    }
}
