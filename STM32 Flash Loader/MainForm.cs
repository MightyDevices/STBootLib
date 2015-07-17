using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Threading;
using System.Media;
using STBootLib;

namespace STUploader
{
    public partial class fMainForm : Form
    {
        /* current file name */
        string fileName;
        /* com port name */
        string portName;
        /* baudrate */
        uint baudRate;
        /* address */
        uint address;
        /* page */
        uint page;

        /* default address */
        const uint baseAddress = 0x08000000;

        /* constructor */
        public fMainForm()
        {
            /* initialize all components */
            InitializeComponent();

            /* set default baurate selection */
            cbBauds.SelectedIndex = 6;
            /* set defaul address */
            address = baseAddress;
        }

        /* drop down list opened */
        private void cbPorts_DropDown(object sender, EventArgs e)
        {
            /* get serial ports */
            var names = SerialPort.GetPortNames();
            /* apply to combo box */
            cbPorts.Items.Clear();
            /* add all com ports */
            cbPorts.Items.AddRange(names);
        }

        /* open file clicked */
        private void bOpenFile_Click(object sender, EventArgs e)
        {
            /* file selected? */
            if (ofdOpen.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                /* set file name */
                tbFileName.Text = ofdOpen.SafeFileName;
                /* set tool tip */
                ttToolTip.SetToolTip(tbFileName, ofdOpen.FileName);
                /* store full path */
                fileName = ofdOpen.FileName;
            }
        }

        /* write clicked */
        private async void bWrite_Click(object sender, EventArgs e)
        {
            /* binary file */
            byte[] bin;
            /* bootloader class instance */
            STBoot stb = new STBoot();

            /* disable button */
            bWrite.Enabled = false;
            /* reset progress */
            UpdateProgress(0);
            /* reset status bar */
            UpdateStatus(false, "");

            /* read file */
            try {
                /* try to open file */
                var s = new FileStream(fileName, FileMode.Open, 
                    FileAccess.Read);
                /* prepare buffer */
                bin = new byte[s.Length];
                /* read file contents */
                await s.ReadAsync(bin, 0, bin.Length);
            /* error during read? */
            } catch (Exception) {
                /* set message */
                UpdateStatus(true, "Error: Unable to read file");
                /* restore button operation */
                bWrite.Enabled = true;
                /* not much to do next */
                return;
            }

            /* perform the operation */
            try {
                /* open the port */
                stb.Open(portName, baudRate);
                /* initialize communication */
                await stb.Initialize();
                /* format message */
                var s = string.Format("Connected: Ver: {0}, PID: 0x{1:X4}",
                    stb.Version, stb.ProductID);
                /* prepare message */
                UpdateStatus(false, s);

                /* accessing flash requires memory erase */
                if (cbxFlash.Checked) {
                    /* erase all necessary pages */
                    for (uint i = 0; i < (bin.Length + 255) / 256; i++)
                        await stb.ErasePage(i + page);
                }

                /* progress reporter */
                var p = new Progress<STBootProgress>(UpateProgress);
                /* write memory */
                await stb.WriteMemory(address, bin, 0, bin.Length, p,
                    CancellationToken.None);
                /* set message */
                UpdateStatus(false, string.Format("Success: {0} bytes written",
                    bin.Length));

                /* go! */
                await stb.Jump(address);
            /* catch all the exceptions here */
            } catch (Exception ex) {
                /* set exception message */
                UpdateStatus(true, "Error: "+ ex.Message);
            /* dispose of port */
            } finally {
                /* close port */
                stb.Close();
                /* re-enable button */
                bWrite.Enabled = true;
            }
        }

        /* port selection was altered */
        private void cbPorts_SelectedIndexChanged(object sender, EventArgs e)
        {
            /* valid selection made? */
            if (cbPorts.SelectedIndex == -1) {
                /* nope, disable button */
                bWrite.Enabled = false;
            } else {
                /* store com port name */
                portName = (string)cbPorts.SelectedItem;
                /* enable button */
                bWrite.Enabled = true;
            }
        }

        /* baud rate changed */
        private void cbBauds_SelectedIndexChanged(object sender, EventArgs e)
        {
            /* convert from string */
            baudRate = uint.Parse((string)cbBauds.SelectedItem);
        }

        /* set focus if button was enabled */
        private void bWrite_EnabledChanged(object sender, EventArgs e)
        {
            /* button was enabled?*/
            if (bWrite.Enabled) {
                /* set focus */
                bWrite.Focus();
            }
        }

        /* parse address */
        private void tbAddress_Leave(object sender, EventArgs e)
        {
            /* parsed address */
            uint addr;

            /* try to parse input */
            try {
                /* convert address field */
                addr = Convert.ToUInt32(tbAddress.Text, 16);
            /* malformed address value */
            } catch (OverflowException) {
                /* set message */
                tsslStatus.Text = "Address too large!";
                /* restore default value */
                addr = baseAddress;
            /* all other errors go here */
            } catch (Exception) {
                /* set message */
                tsslStatus.Text = "Incorrect hex value";
                /* restore default value */
                addr = baseAddress;
            }

            /* store realigned address */
            address = addr & 0xffffff00;
            /* start page - end page */
            page = (address - baseAddress) / 256;

            /* rewrite */
            tbAddress.Text = string.Format("0x{0:X8}", address);
        }

        /* set current progress */
        private void UpateProgress(STBootProgress p)
        {
            /* converts bytes to percentage */
            UpdateProgress(100 * p.bytesProcessed / p.bytesTotal);
        }

        /* set current progress */
        private void UpdateProgress(int percent)
        {
            /* set progress bar value */
            pbProgress.Value = percent;
            /* set label */
            lProgress.Text = percent.ToString() + "%";
        }

        /* update status bar */
        private void UpdateStatus(bool ding, string text)
        {
            /* text */
            tsslStatus.Text = text;
            /* play a system sound? */
            if (ding) {
                /* ^^ ding! */
                SystemSounds.Exclamation.Play();
            }
        }

        private void cbPorts_KeyDown(object sender, KeyEventArgs e)
        {
            cbPorts.DroppedDown = true;
        }
    }
}
