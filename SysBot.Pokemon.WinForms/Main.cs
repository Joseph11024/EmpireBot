using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Z3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Pokemon.Helpers;
using System.Drawing;
using SysBot.Pokemon.WinForms.Properties;

namespace SysBot.Pokemon.WinForms;

public sealed partial class Main : Form
{
    private readonly List<PokeBotState> Bots = [];


    private IPokeBotRunner RunningEnvironment { get; set; }
    private ProgramConfig Config { get; set; }

    private bool _isFormLoading = true;
    public Main()
    {
        InitializeComponent();
        comboBox1.SelectedIndexChanged += new EventHandler(ComboBox1_SelectedIndexChanged);
        Load += async (sender, e) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();

        // Update checker
        UpdateChecker updateChecker = new UpdateChecker();
        await UpdateChecker.CheckForUpdatesAsync();

        if (File.Exists(Program.ConfigPath))
        {
            var lines = File.ReadAllText(Program.ConfigPath);
            Config = JsonSerializer.Deserialize(lines, ProgramConfigContext.Default.ProgramConfig) ?? new ProgramConfig();
            LogConfig.MaxArchiveFiles = Config.Hub.MaxArchiveFiles;
            LogConfig.LoggingEnabled = Config.Hub.LoggingEnabled;
            comboBox1.SelectedValue = (int)Config.Mode;
            RunningEnvironment = GetRunner(Config);
            foreach (var bot in Config.Bots)
            {
                bot.Initialize();
                AddBot(bot);
            }
        }
        else
        {
            Config = new ProgramConfig();
            RunningEnvironment = GetRunner(Config);
            Config.Hub.Folder.CreateDefaults(Program.WorkingDirectory);
        }

        RTB_Logs.MaxLength = 32_767; // character length
        LoadControls();
        Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "NotPaldea.net" : Config.Hub.BotName)} {TradeBot.Version} ({Config.Mode})";
        Task.Run(BotMonitor);
        InitUtil.InitializeStubs(Config.Mode);
        _isFormLoading = false;
        UpdateBackgroundImage(Config.Mode);
    }

    private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
    {
        ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(cfg.Hub, new BotFactory8SWSH()),
        ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(cfg.Hub, new BotFactory8BS()),
        ProgramMode.LA => new PokeBotRunnerImpl<PA8>(cfg.Hub, new BotFactory8LA()),
        ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
        ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(cfg.Hub, new BotFactory7LGPE()),
        _ => throw new IndexOutOfRangeException("Unsupported mode."),
    };

    private async Task BotMonitor()
    {
        while (!Disposing)
        {
            try
            {
                foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    c.ReadState();
            }
            catch
            {
                // Updating the collection by adding/removing bots will change the iterator
                // Can try a for-loop or ToArray, but those still don't prevent concurrent mutations of the array.
                // Just try, and if failed, ignore. Next loop will be fine. Locks on the collection are kinda overkill, since this task is not critical.
            }
            await Task.Delay(2_000).ConfigureAwait(false);
        }
    }

    private void LoadControls()
    {
        MinimumSize = Size;
        PG_Hub.SelectedObject = RunningEnvironment.Config;

        var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType))).Where(z => RunningEnvironment.SupportsRoutine(z));
        var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Routine.DisplayMember = nameof(ComboItem.Text);
        CB_Routine.ValueMember = nameof(ComboItem.Value);
        CB_Routine.DataSource = list;
        CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade; // default option

        var protocols = (SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol));
        var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Protocol.DisplayMember = nameof(ComboItem.Text);
        CB_Protocol.ValueMember = nameof(ComboItem.Value);
        CB_Protocol.DataSource = listP;
        CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi; // default option
                                                              // Populate the game mode dropdown
        var gameModes = Enum.GetValues(typeof(ProgramMode))
            .Cast<ProgramMode>()
            .Where(m => m != ProgramMode.None) // Exclude the 'None' value
            .Select(mode => new { Text = mode.ToString(), Value = (int)mode })
            .ToList();

        comboBox1.DisplayMember = "Text";
        comboBox1.ValueMember = "Value";
        comboBox1.DataSource = gameModes;

        // Set the current mode as selected in the dropdown
        comboBox1.SelectedValue = (int)Config.Mode;

        comboBox2.Items.Add("Light Mode");
        comboBox2.Items.Add("Dark Mode");
        comboBox2.Items.Add("Poke Mode");
        comboBox2.Items.Add("Gengar Mode");
        comboBox2.Items.Add("Sylveon Mode");

        // Load the current theme from configuration and set it in the comboBox2
        string theme = Config.Hub.ThemeOption;
        if (string.IsNullOrEmpty(theme) || !comboBox2.Items.Contains(theme))
        {
            comboBox2.SelectedIndex = 0;  // Set default selection to Light Mode if ThemeOption is empty or invalid
        }
        else
        {
            comboBox2.SelectedItem = theme;  // Set the selected item in the combo box based on ThemeOption
        }
        switch (theme)
        {
            case "Dark Mode":
                ApplyDarkTheme();
                break;
            case "Light Mode":
                ApplyLightTheme();
                break;
            case "Poke Mode":
                ApplyPokemonTheme();
                break;
            case "Gengar Mode":
                ApplyGengarTheme();
                break;
            case "Sylveon Mode":
                ApplySylveonTheme();
                break;
            default:
                ApplyGengarTheme();
                break;
        }

        LogUtil.Forwarders.Add(new TextBoxForwarder(RTB_Logs));
    }

    private ProgramConfig GetCurrentConfiguration()
    {
        Config.Bots = [.. Bots];
        return Config;
    }

    private void Main_FormClosing(object sender, FormClosingEventArgs e)
    {

        SaveCurrentConfig();
        var bots = RunningEnvironment;
        if (!bots.IsRunning)
            return;

        async Task WaitUntilNotRunning()
        {
            while (bots.IsRunning)
                await Task.Delay(10).ConfigureAwait(false);
        }

        // Try to let all bots hard-stop before ending execution of the entire program.
        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;
        bots.StopAll();
        Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
    }

    private void SaveCurrentConfig()
    {
        var cfg = GetCurrentConfiguration();
        var lines = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
        File.WriteAllText(Program.ConfigPath, lines);
    }

    [JsonSerializable(typeof(ProgramConfig))]
    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    public sealed partial class ProgramConfigContext : JsonSerializerContext;
    private void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_isFormLoading) return; // Check to avoid processing during form loading

        if (comboBox1.SelectedValue is int selectedValue)
        {
            ProgramMode newMode = (ProgramMode)selectedValue;
            Config.Mode = newMode;

            SaveCurrentConfig();
            UpdateRunnerAndUI();

            UpdateBackgroundImage(newMode);
        }
    }

    private void UpdateRunnerAndUI()
    {
        RunningEnvironment = GetRunner(Config);
        Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "NotPaldea.net" : Config.Hub.BotName)} {TradeBot.Version} ({Config.Mode})";
    }

    private void B_Start_Click(object sender, EventArgs e)
    {
        SaveCurrentConfig();

        LogUtil.LogInfo("Starting all bots...", "Form");
        RunningEnvironment.InitializeStart();
        SendAll(BotControlCommand.Start);
        Tab_Logs.Select();

        if (Bots.Count == 0)
            WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
    }

    private void B_RebootStop_Click(object sender, EventArgs e)
    {
        B_Stop_Click(sender, e);
        Task.Run(async () =>
        {
            await Task.Delay(3_500).ConfigureAwait(false);
            SaveCurrentConfig();
            LogUtil.LogInfo("Restarting all the consoles...", "Form");
            RunningEnvironment.InitializeStart();
            SendAll(BotControlCommand.RebootAndStop);
            await Task.Delay(5_000).ConfigureAwait(false); // Add a delay before restarting the bot
            SendAll(BotControlCommand.Start); // Start the bot after the delay
            Tab_Logs.Select();
            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the reboot command.");
        });
    }

    private void UpdateBackgroundImage(ProgramMode mode)
    {
        FLP_Bots.BackgroundImage = mode switch
        {
            ProgramMode.SV => Resources.sv_mode_image,
            ProgramMode.SWSH => Resources.swsh_mode_image,
            ProgramMode.BDSP => Resources.bdsp_mode_image,
            ProgramMode.LA => Resources.pla_mode_image,
            ProgramMode.LGPE => Resources.lgpe_mode_image,
            _ => null,
        };
        FLP_Bots.BackgroundImageLayout = ImageLayout.Stretch;
    }

    private void SendAll(BotControlCommand cmd)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.SendCommand(cmd, false);
    }

    private void B_Stop_Click(object sender, EventArgs e)
    {
        var env = RunningEnvironment;
        if (!env.IsRunning && (ModifierKeys & Keys.Alt) == 0)
        {
            WinFormsUtil.Alert("Nothing is currently running.");
            return;
        }

        var cmd = BotControlCommand.Stop;

        if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // either, because remembering which can be hard
        {
            if (env.IsRunning)
            {
                WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                cmd = BotControlCommand.Idle;
            }
            else
            {
                WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                cmd = BotControlCommand.Resume;
            }
        }
        else
        {
            env.StopAll();
        }
        SendAll(cmd);
    }

    private void B_New_Click(object sender, EventArgs e)
    {
        var cfg = CreateNewBotConfig();
        if (!AddBot(cfg))
        {
            WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
            return;
        }
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async void Updater_Click(object sender, EventArgs e)
    {
        var (updateAvailable, updateRequired, newVersion) = await UpdateChecker.CheckForUpdatesAsync();
        if (updateAvailable)
        {
            UpdateForm updateForm = new UpdateForm(updateRequired, newVersion);
            updateForm.ShowDialog();
        }
        else
        {
            MessageBox.Show("No updates are available.", "Update Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private bool AddBot(PokeBotState cfg)
    {
        if (!cfg.IsValid())
            return false;

        if (Bots.Any(z => z.Connection.Equals(cfg.Connection)))
            return false;

        PokeRoutineExecutorBase newBot;
        try
        {
            Console.WriteLine($"Current Mode ({Config.Mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
            newBot = RunningEnvironment.CreateBotFromConfig(cfg);
        }
        catch
        {
            return false;
        }

        try
        {
            RunningEnvironment.Add(newBot);
        }
        catch (ArgumentException ex)
        {
            WinFormsUtil.Error(ex.Message);
            return false;
        }

        AddBotControl(cfg);
        Bots.Add(cfg);
        return true;
    }

    private void AddBotControl(PokeBotState cfg)
    {
        var row = new BotController { Width = FLP_Bots.Width };
        row.Initialize(RunningEnvironment, cfg);
        FLP_Bots.Controls.Add(row);
        FLP_Bots.SetFlowBreak(row, true);
        row.Click += (s, e) =>
        {
            var details = cfg.Connection;
            TB_IP.Text = details.IP;
            NUD_Port.Value = details.Port;
            CB_Protocol.SelectedIndex = (int)details.Protocol;
            CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
        };

        row.Remove += (s, e) =>
        {
            Bots.Remove(row.State);
            RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
            FLP_Bots.Controls.Remove(row);
        };
    }

    private PokeBotState CreateNewBotConfig()
    {
        var ip = TB_IP.Text;
        var port = (int)NUD_Port.Value;
        var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
        cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

        var pk = new PokeBotState { Connection = cfg };
        var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
        pk.Initialize(type);
        return pk;
    }

    private void FLP_Bots_Resize(object sender, EventArgs e)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.Width = FLP_Bots.Width;
    }

    private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
    {
        TB_IP.Visible = CB_Protocol.SelectedIndex == 0;
    }

    private void ComboBox2_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            string selectedTheme = comboBox.SelectedItem.ToString();
            Config.Hub.ThemeOption = selectedTheme;  // Save the selected theme to the config
            SaveCurrentConfig();  // Save the config to file

            switch (selectedTheme)
            {
                case "Light Mode":
                    ApplyLightTheme();
                    break;
                case "Dark Mode":
                    ApplyDarkTheme();
                    break;
                case "Poke Mode":
                    ApplyPokemonTheme();
                    break;
                case "Gengar Mode":
                    ApplyGengarTheme();
                    break;
                case "Sylveon Mode":
                    ApplySylveonTheme();
                    break;
                default:
                    ApplyGengarTheme();
                    break;
            }
        }
    }

    private void ApplySylveonTheme()
    {
        // Define Sylveon-theme colors
        Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
        Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
        Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
        Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
        Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
        Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
        Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
        Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
        Color UpdateGray = Color.FromArgb(54, 69, 79); // Update Button
        Color TextBlack = Color.FromArgb(0, 0, 0); // Button Text
        Color ButtonGrey = Color.FromArgb(232, 232, 232); // Button Background
        Color TextLime = Color.FromArgb(64, 255, 25); // Bot Text

        // Set the background color of the form
        BackColor = ElegantWhite;

        // Set the foreground color of the form (text color)
        ForeColor = TextLime;

        // Set the background color of the tab control
        TC_Main.BackColor = SkyBlue;

        // Set the background color of each tab page
        foreach (TabPage page in TC_Main.TabPages)
        {
            page.BackColor = ElegantWhite;
        }

        // Set the background color of the property grid
        PG_Hub.BackColor = ElegantWhite;
        PG_Hub.LineColor = ButtonGrey;
        PG_Hub.CategoryForeColor = TextBlack;
        PG_Hub.CategorySplitterColor = ButtonGrey;
        PG_Hub.HelpBackColor = ElegantWhite;
        PG_Hub.HelpForeColor = TextBlack;
        PG_Hub.ViewBackColor = ElegantWhite;
        PG_Hub.ViewForeColor = TextBlack;

        // Set the background color of the rich text box
        RTB_Logs.BackColor = ElegantWhite;
        RTB_Logs.ForeColor = TextBlack;

        // Set colors for other controls
        TB_IP.BackColor = ButtonGrey;
        TB_IP.ForeColor = TextBlack;

        CB_Routine.BackColor = ButtonGrey;
        CB_Routine.ForeColor = TextBlack;

        NUD_Port.BackColor = ButtonGrey;
        NUD_Port.ForeColor = TextBlack;

        B_New.BackColor = ButtonGrey;
        B_New.ForeColor = TextBlack;

        FLP_Bots.BackColor = ElegantWhite;

        CB_Protocol.BackColor = ButtonGrey;
        CB_Protocol.ForeColor = TextBlack;

        comboBox1.BackColor = ButtonGrey;
        comboBox1.ForeColor = TextBlack;

        B_Stop.BackColor = ButtonGrey;
        B_Stop.ForeColor = TextBlack;

        B_Start.BackColor = ButtonGrey;
        B_Start.ForeColor = TextBlack;

        B_RebootStop.BackColor = ButtonGrey;
        B_RebootStop.ForeColor = TextBlack;
    }

    private void ApplyGengarTheme()
    {
        // Define Gengar-theme colors
        Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
        Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
        Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
        Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
        Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
        Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
        Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
        Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
        Color UpdateGray = Color.FromArgb(54, 69, 79); // Update Button
        Color TextBlack = Color.FromArgb(0, 0, 0); // Button Text
        Color ButtonGrey = Color.FromArgb(232, 232, 232); // Button Background
        Color TextLime = Color.FromArgb(64, 255, 25); // Bot Text

        // Set the background color of the form
        BackColor = ElegantWhite;

        // Set the foreground color of the form (text color)
        ForeColor = TextLime;

        // Set the background color of the tab control
        TC_Main.BackColor = SkyBlue;

        // Set the background color of each tab page
        foreach (TabPage page in TC_Main.TabPages)
        {
            page.BackColor = ElegantWhite;
        }

        // Set the background color of the property grid
        PG_Hub.BackColor = ElegantWhite;
        PG_Hub.LineColor = ButtonGrey;
        PG_Hub.CategoryForeColor = TextBlack;
        PG_Hub.CategorySplitterColor = ButtonGrey;
        PG_Hub.HelpBackColor = ElegantWhite;
        PG_Hub.HelpForeColor = TextBlack;
        PG_Hub.ViewBackColor = ElegantWhite;
        PG_Hub.ViewForeColor = TextBlack;

        // Set the background color of the rich text box
        RTB_Logs.BackColor = ElegantWhite;
        RTB_Logs.ForeColor = TextBlack;

        // Set colors for other controls
        TB_IP.BackColor = ButtonGrey;
        TB_IP.ForeColor = TextBlack;

        CB_Routine.BackColor = ButtonGrey;
        CB_Routine.ForeColor = TextBlack;

        NUD_Port.BackColor = ButtonGrey;
        NUD_Port.ForeColor = TextBlack;

        B_New.BackColor = ButtonGrey;
        B_New.ForeColor = TextBlack;

        FLP_Bots.BackColor = ElegantWhite;

        CB_Protocol.BackColor = ButtonGrey;
        CB_Protocol.ForeColor = TextBlack;

        comboBox1.BackColor = ButtonGrey;
        comboBox1.ForeColor = TextBlack;

        B_Stop.BackColor = ButtonGrey;
        B_Stop.ForeColor = TextBlack;

        B_Start.BackColor = ButtonGrey;
        B_Start.ForeColor = TextBlack;

        B_RebootStop.BackColor = ButtonGrey;
        B_RebootStop.ForeColor = TextBlack;
    }

    private void ApplyLightTheme()
    {
        // Define the color palette
        Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
        Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
        Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
        Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
        Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
        Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
        Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
        Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
        Color UpdateGray = Color.FromArgb(54, 69, 79); // Update Button
        Color TextBlack = Color.FromArgb(0, 0, 0); // Button Text
        Color ButtonGrey = Color.FromArgb(232, 232, 232); // Button Background
        Color TextLime = Color.FromArgb(64, 255, 25); // Bot Text

        // Set the background color of the form
        BackColor = ElegantWhite;

        // Set the foreground color of the form (text color)
        ForeColor = TextLime;

        // Set the background color of the tab control
        TC_Main.BackColor = SkyBlue;

        // Set the background color of each tab page
        foreach (TabPage page in TC_Main.TabPages)
        {
            page.BackColor = ElegantWhite;
        }

        // Set the background color of the property grid
        PG_Hub.BackColor = ElegantWhite;
        PG_Hub.LineColor = ButtonGrey;
        PG_Hub.CategoryForeColor = TextBlack;
        PG_Hub.CategorySplitterColor = ButtonGrey;
        PG_Hub.HelpBackColor = ElegantWhite;
        PG_Hub.HelpForeColor = TextBlack;
        PG_Hub.ViewBackColor = ElegantWhite;
        PG_Hub.ViewForeColor = TextBlack;

        // Set the background color of the rich text box
        RTB_Logs.BackColor = ElegantWhite;
        RTB_Logs.ForeColor = TextBlack;

        // Set colors for other controls
        TB_IP.BackColor = ButtonGrey;
        TB_IP.ForeColor = TextBlack;

        CB_Routine.BackColor = ButtonGrey;
        CB_Routine.ForeColor = TextBlack;

        NUD_Port.BackColor = ButtonGrey;
        NUD_Port.ForeColor = TextBlack;

        B_New.BackColor = ButtonGrey;
        B_New.ForeColor = TextBlack;

        FLP_Bots.BackColor = ElegantWhite;

        CB_Protocol.BackColor = ButtonGrey;
        CB_Protocol.ForeColor = TextBlack;

        comboBox1.BackColor = ButtonGrey;
        comboBox1.ForeColor = TextBlack;

        B_Stop.BackColor = ButtonGrey;
        B_Stop.ForeColor = TextBlack;

        B_Start.BackColor = ButtonGrey;
        B_Start.ForeColor = TextBlack;

        B_RebootStop.BackColor = ButtonGrey;
        B_RebootStop.ForeColor = TextBlack;
    }

    private void ApplyPokemonTheme()
    {
        // Define Poke-theme colors
        Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
        Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
        Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
        Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
        Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
        Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
        Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
        Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
        Color UpdateGray = Color.FromArgb(54, 69, 79); // Update Button
        Color TextBlack = Color.FromArgb(0, 0, 0); // Button Text
        Color ButtonGrey = Color.FromArgb(232, 232, 232); // Button Background
        Color TextLime = Color.FromArgb(64, 255, 25); // Bot Text

        // Set the background color of the form
        BackColor = ElegantWhite;

        // Set the foreground color of the form (text color)
        ForeColor = TextLime;

        // Set the background color of the tab control
        TC_Main.BackColor = SkyBlue;

        // Set the background color of each tab page
        foreach (TabPage page in TC_Main.TabPages)
        {
            page.BackColor = ElegantWhite;
        }

        // Set the background color of the property grid
        PG_Hub.BackColor = ElegantWhite;
        PG_Hub.LineColor = ButtonGrey;
        PG_Hub.CategoryForeColor = TextBlack;
        PG_Hub.CategorySplitterColor = ButtonGrey;
        PG_Hub.HelpBackColor = ElegantWhite;
        PG_Hub.HelpForeColor = TextBlack;
        PG_Hub.ViewBackColor = ElegantWhite;
        PG_Hub.ViewForeColor = TextBlack;

        // Set the background color of the rich text box
        RTB_Logs.BackColor = ElegantWhite;
        RTB_Logs.ForeColor = TextBlack;

        // Set colors for other controls
        TB_IP.BackColor = ButtonGrey;
        TB_IP.ForeColor = TextBlack;

        CB_Routine.BackColor = ButtonGrey;
        CB_Routine.ForeColor = TextBlack;

        NUD_Port.BackColor = ButtonGrey;
        NUD_Port.ForeColor = TextBlack;

        B_New.BackColor = ButtonGrey;
        B_New.ForeColor = TextBlack;

        FLP_Bots.BackColor = ElegantWhite;

        CB_Protocol.BackColor = ButtonGrey;
        CB_Protocol.ForeColor = TextBlack;

        comboBox1.BackColor = ButtonGrey;
        comboBox1.ForeColor = TextBlack;

        B_Stop.BackColor = ButtonGrey;
        B_Stop.ForeColor = TextBlack;

        B_Start.BackColor = ButtonGrey;
        B_Start.ForeColor = TextBlack;

        B_RebootStop.BackColor = ButtonGrey;
        B_RebootStop.ForeColor = TextBlack;
    }

    private void ApplyDarkTheme()
    {
        // Define the dark theme colors
        Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
        Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
        Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
        Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
        Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
        Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
        Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
        Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
        Color UpdateGray = Color.FromArgb(54, 69, 79); // Update Button
        Color TextBlack = Color.FromArgb(0, 0, 0); // Button Text
        Color ButtonGrey = Color.FromArgb(232, 232, 232); // Button Background
        Color TextLime = Color.FromArgb(64, 255, 25); // Bot Text

        // Set the background color of the form
        BackColor = ElegantWhite;

        // Set the foreground color of the form (text color)
        ForeColor = TextLime;

        // Set the background color of the tab control
        TC_Main.BackColor = SkyBlue;

        // Set the background color of each tab page
        foreach (TabPage page in TC_Main.TabPages)
        {
            page.BackColor = ElegantWhite;
        }

        // Set the background color of the property grid
        PG_Hub.BackColor = ElegantWhite;
        PG_Hub.LineColor = ButtonGrey;
        PG_Hub.CategoryForeColor = TextBlack;
        PG_Hub.CategorySplitterColor = ButtonGrey;
        PG_Hub.HelpBackColor = ElegantWhite;
        PG_Hub.HelpForeColor = TextBlack;
        PG_Hub.ViewBackColor = ElegantWhite;
        PG_Hub.ViewForeColor = TextBlack;

        // Set the background color of the rich text box
        RTB_Logs.BackColor = ElegantWhite;
        RTB_Logs.ForeColor = TextBlack;

        // Set colors for other controls
        TB_IP.BackColor = ButtonGrey;
        TB_IP.ForeColor = TextBlack;

        CB_Routine.BackColor = ButtonGrey;
        CB_Routine.ForeColor = TextBlack;

        NUD_Port.BackColor = ButtonGrey;
        NUD_Port.ForeColor = TextBlack;

        B_New.BackColor = ButtonGrey;
        B_New.ForeColor = TextBlack;

        FLP_Bots.BackColor = ElegantWhite;

        CB_Protocol.BackColor = ButtonGrey;
        CB_Protocol.ForeColor = TextBlack;

        comboBox1.BackColor = ButtonGrey;
        comboBox1.ForeColor = TextBlack;

        B_Stop.BackColor = ButtonGrey;
        B_Stop.ForeColor = TextBlack;

        B_Start.BackColor = ButtonGrey;
        B_Start.ForeColor = TextBlack;

        B_RebootStop.BackColor = ButtonGrey;
        B_RebootStop.ForeColor = TextBlack;
    }
}

