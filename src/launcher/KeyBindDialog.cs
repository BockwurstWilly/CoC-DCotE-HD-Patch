using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoCHD
{
    /// <summary>"Press a key": captures one key, mouse button or wheel step as the game's DirectInput code.</summary>
    class KeyBindDialog : Form
    {
        public GameKey? Result { get; private set; }

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        // Del: a short press binds Del, holding it for a second clears the binding
        const int ClearHoldMs = 1000;
        readonly Timer delTimer = new Timer { Interval = 50 };
        DateTime delDown;
        Label hint;

        public KeyBindDialog(GameAction action)
        {
            Text = action.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(480, 130);
            KeyPreview = true;

            var title = Theme.Label(action.Name, Theme.Display(14f), Theme.Brass);
            title.Location = new Point(20, 16);
            hint = Theme.Label(T.Get("keys_press"), Theme.Body, Theme.Text);
            hint.Location = new Point(20, 56);
            var esc = Theme.Label(T.Get("keys_esc"), Theme.Small, Theme.TextDim);
            esc.Location = new Point(20, 86);
            Controls.AddRange(new Control[] { title, hint, esc });
            foreach (Control c in Controls)
                c.MouseDown += (o, e) => OnMouseDown(e);
            delTimer.Tick += (o, e) => DelTick();
            Theme.ScaleToDpi(this);
        }

        // keys like Tab, arrows and Alt never reach KeyDown of a form: take everything here
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN)
                return base.ProcessCmdKey(ref msg, keyData);
            var vk = keyData & Keys.KeyCode;
            if (vk == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }
            if (vk == Keys.Delete)
            {
                // auto-repeat while held (bit 30: key was already down) - the timer decides
                if ((((long)msg.LParam >> 30) & 1) == 0)
                {
                    delDown = DateTime.UtcNow;
                    delTimer.Start();
                }
                return true;
            }
            int dik = ToDik(vk, ((long)msg.LParam & (1 << 24)) != 0, (int)(((long)msg.LParam >> 16) & 0xFF));
            if (dik > 0)
                Finish(GameKey.Key(dik));
            return true;
        }

        static int ToDik(Keys vk, bool extended, int scan)
        {
            // left/right of the modifiers: Windows reports the generic key
            switch (vk)
            {
                case Keys.ShiftKey: return (GetAsyncKeyState(0xA1) & 0x8000) != 0 ? 0x36 : 0x2A;
                case Keys.ControlKey: return (GetAsyncKeyState(0xA3) & 0x8000) != 0 ? 0x9D : 0x1D;
                case Keys.Menu: return (GetAsyncKeyState(0xA5) & 0x8000) != 0 ? 0xB8 : 0x38;
            }
            if (scan == 0)
                scan = (int)MapVirtualKey((uint)vk, 0);
            if (scan == 0 || scan > 0x7F)
                return 0;
            // DirectInput key codes are the set-1 scan codes, extended keys with bit 7 set
            return scan | (extended ? 0x80 : 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left: Finish(GameKey.Button(GameKey.MouseLeft)); break;
                case MouseButtons.Right: Finish(GameKey.Button(GameKey.MouseRight)); break;
                case MouseButtons.Middle: Finish(GameKey.Button(GameKey.MouseMiddle)); break;
                case MouseButtons.XButton1: Finish(GameKey.Button(GameKey.Mouse4)); break;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Finish(GameKey.Button(e.Delta > 0 ? GameKey.WheelUp : GameKey.WheelDown));
        }

        void DelTick()
        {
            double held = (DateTime.UtcNow - delDown).TotalMilliseconds;
            if ((GetAsyncKeyState((int)Keys.Delete) & 0x8000) == 0)
            {
                delTimer.Stop();
                Finish(GameKey.Key(0xD3)); // released early: bind Del itself
            }
            else if (held >= ClearHoldMs)
            {
                delTimer.Stop();
                Finish(GameKey.None);
            }
            else
                hint.Text = T.Get("keys_press") + "  " + new string('■', 1 + (int)(held * 5 / ClearHoldMs));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                delTimer.Dispose();
            base.Dispose(disposing);
        }

        void Finish(GameKey k)
        {
            Result = k;
            DialogResult = DialogResult.OK;
        }
    }
}
