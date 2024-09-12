/*
 * SetupDialogForm.cs
 * Copyright (C) 2024 - Present, Julien Lecomte - All Rights Reserved
 * Licensed under the MIT License. See the accompanying LICENSE file for terms.
 */

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ASCOM.DarkSkyGeek
{
    // Form not registered for COM!
    [ComVisible(false)]

    public partial class SetupDialogForm : Form
    {
        // Holder for a reference to the driver's trace logger
        readonly TelescopeCoverV2 device;

        public SetupDialogForm(TelescopeCoverV2 device)
        {
            InitializeComponent();

            // Save the provided trace logger for use within the setup dialogue
            this.device = device;
        }

        private void SetupDialogForm_Load(object sender, EventArgs e)
        {
            chkAutoDetect.Checked = device.autoDetectComPort;

            comboBoxComPort.Enabled = !chkAutoDetect.Checked;

            // Set the list of COM ports to those that are currently available
            comboBoxComPort.Items.Clear();
            // Use System.IO because it's static
            comboBoxComPort.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
            // Select the current port if possible
            if (device.comPortOverride != null && comboBoxComPort.Items.Contains(device.comPortOverride))
            {
                comboBoxComPort.SelectedItem = device.comPortOverride;
            }

            chkTrace.Checked = device.tl.Enabled;
        }

        private void CmdOK_Click(object sender, EventArgs e)
        {
            device.tl.Enabled = chkTrace.Checked;
            device.autoDetectComPort = chkAutoDetect.Checked;
            device.comPortOverride = (string)comboBoxComPort.SelectedItem;
        }

        private void CmdCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ChkAutoDetect_CheckedChanged(object sender, EventArgs e)
        {
            comboBoxComPort.Enabled = !((CheckBox)sender).Checked;
        }

        private void BrowseToHomepage(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("https://github.com/jlecomte/ascom-telescope-cover-v2");
            }
            catch (System.ComponentModel.Win32Exception noBrowser)
            {
                if (noBrowser.ErrorCode == -2147467259)
                    MessageBox.Show(noBrowser.Message);
            }
            catch (System.Exception other)
            {
                MessageBox.Show(other.Message);
            }
        }
    }
}