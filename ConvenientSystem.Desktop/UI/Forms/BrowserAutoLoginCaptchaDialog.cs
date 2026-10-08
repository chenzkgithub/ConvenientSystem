namespace ConvenientSystem;

/// <summary>显示 ERP 图片验证码并返回用户输入。</summary>
internal sealed class BrowserAutoLoginCaptchaDialog : Form
{
    private readonly TextBox _codeTextBox;

    public string VerifyCode => _codeTextBox.Text.Trim();

    public BrowserAutoLoginCaptchaDialog(string imageBase64)
    {
        Text = "ERP 登录验证";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ClientSize = new Size(360, 190);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        Controls.Add(new Label
        {
            Text = "请输入下图中的验证码",
            Bounds = new Rectangle(28, 20, 300, 24),
        });

        var pictureBox = new PictureBox
        {
            Bounds = new Rectangle(28, 54, 150, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(245, 245, 245),
        };
        pictureBox.Image = DecodeImage(imageBase64);
        Controls.Add(pictureBox);

        _codeTextBox = new TextBox
        {
            Bounds = new Rectangle(194, 64, 132, 28),
            MaxLength = 8,
        };
        Controls.Add(_codeTextBox);

        var loginButton = new Button
        {
            Text = "登录",
            Bounds = new Rectangle(160, 132, 78, 32),
            BackColor = Color.FromArgb(47, 169, 143),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        loginButton.FlatAppearance.BorderSize = 0;
        loginButton.Click += (_, _) =>
        {
            if (VerifyCode.Length == 0)
            {
                MessageBox.Show(this, "请输入验证码。", "ERP 登录验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(loginButton);

        var cancelButton = new Button
        {
            Text = "取消",
            Bounds = new Rectangle(248, 132, 78, 32),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(cancelButton);

        AcceptButton = loginButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _codeTextBox.Focus();
        FormClosed += (_, _) => pictureBox.Image?.Dispose();
    }

    private static Image DecodeImage(string imageBase64)
    {
        var bytes = Convert.FromBase64String(imageBase64);
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
