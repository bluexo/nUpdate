// Author: Dominic Beger (Trade/ProgTrade)

using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using nUpdate.Administration.Core.Win32;

namespace nUpdate.Administration.UI.Controls
{
    public class CommandLink : Button
    {
        private const int BS_COMMANDLINK = 0x0000000E;
        private const uint BCM_SETNOTE = 0x00001609;
        private const uint BCM_GETNOTE = 0x0000160A;
        private const uint BCM_GETNOTELENGTH = 0x0000160B;
        private const uint BCM_SETSHIELD = 0x0000160C;
        private bool _shield;

        public CommandLink()
        {
            FlatStyle = FlatStyle.System;
        }

        protected override Size DefaultSize
        {
            get { return new Size(180, 60); }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cParams = base.CreateParams;
                cParams.Style |= BS_COMMANDLINK;
                return cParams;
            }
        }

        [Category("Command Link"),
         Description("Gets or sets the shield icon visibility of the command link."),
         DefaultValue(false)]
        public bool Shield
        {
            get { return _shield; }
            set
            {
                _shield = value;
                NativeMethods.SendMessage(new HandleRef(this, Handle), BCM_SETSHIELD, IntPtr.Zero, _shield);
            }
        }

        [Category("Command Link"),
         Description("Gets or sets the note text of the command link."),
         DefaultValue("")]
        public string Note
        {
            get { return GetNoteText(); }
            set { SetNoteText(value); }
        }

        private void SetNoteText(string value)
        {
            NativeMethods.SendMessage(new HandleRef(this, Handle), BCM_SETNOTE, IntPtr.Zero, value);
        }

        private string GetNoteText()
        {
            var length =
                NativeMethods.SendMessage(new HandleRef(this, Handle), BCM_GETNOTELENGTH, IntPtr.Zero, IntPtr.Zero)
                    .ToInt32() + 1;

            var sb = new StringBuilder(length);
            NativeMethods.SendMessage(new HandleRef(this, Handle), BCM_GETNOTE, ref length, sb);
            return sb.ToString();
        }
    }
}