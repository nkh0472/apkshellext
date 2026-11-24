using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ApkShellext2.Properties;

namespace ApkShellext2.Shell {
    /// <summary>
    /// Modeless progress window for a CSV export. It runs on the <see cref="StaUiHost"/> pump
    /// thread so the export itself can continue on a background thread; closing the window or
    /// pressing Cancel stops the export after the file that is being read.
    /// </summary>
    internal sealed class CsvExportProgressForm : Form {
        private readonly CancellationTokenSource cancellation;
        private readonly Label status;

        public CsvExportProgressForm(int total, CancellationTokenSource cancellation) {
            this.cancellation = cancellation;

            Text = Resources.strExportCsvTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(380, 108);

            status = new Label {
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(348, 32),
                Text = string.Format(Resources.strExportCsvProgress, 0, total)
            };

            var cancel = new Button {
                Text = Resources.btnCancel,
                Location = new Point(272, 64),
                Size = new Size(88, 28)
            };
            cancel.Click += (s, e) => {
                this.cancellation.Cancel();
                cancel.Enabled = false;
            };

            Controls.Add(status);
            Controls.Add(cancel);

            FormClosing += (s, e) => {
                if (!this.cancellation.IsCancellationRequested) this.cancellation.Cancel();
            };
        }

        public void SetProgress(int done, int total) {
            if (IsDisposed) return;
            status.Text = string.Format(Resources.strExportCsvProgress, done, total);
        }
    }
}
