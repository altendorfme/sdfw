using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Sdfw.Core.Models;
using Wpf.Ui.Controls;
using Sdfw.Ui.Localization;

namespace Sdfw.Ui.Views;

/// <summary>
/// Window for adding or editing DNS providers.
/// </summary>
public partial class ProviderDialog : FluentWindow
{
    private DnsProvider? _provider;
    private bool _isNew;

    /// <summary>
    /// Gets the result provider if the user clicked Save.
    /// </summary>
    public DnsProvider? ResultProvider { get; private set; }

    /// <summary>
    /// Gets whether the dialog was confirmed (Save was clicked).
    /// </summary>
    public bool Confirmed { get; private set; }

    public ProviderDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures the dialog for adding a new provider.
    /// </summary>
    public void ConfigureForAdd()
    {
        _isNew = true;
        _provider = new DnsProvider();
        Title = "Adicionar Provedor DNS";
        DialogTitle.Text = "Adicionar Provedor DNS";
        ClearFields();
    }

    /// <summary>
    /// Configures the dialog for editing an existing provider.
    /// </summary>
    public void ConfigureForEdit(DnsProvider provider)
    {
        _isNew = false;
        _provider = provider;
        Title = "Editar Provedor DNS";
        DialogTitle.Text = "Editar Provedor DNS";
        PopulateFields(provider);
    }

    private void ClearFields()
    {
        NameTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        TypeComboBox.SelectedIndex = 0;
        PrimaryIpv4TextBox.Text = string.Empty;
        SecondaryIpv4TextBox.Text = string.Empty;
        PrimaryIpv6TextBox.Text = string.Empty;
        SecondaryIpv6TextBox.Text = string.Empty;
        DohUrlTextBox.Text = string.Empty;
        BootstrapIpsTextBox.Text = string.Empty;
        UpdateTypeVisibility();
    }

    private void PopulateFields(DnsProvider provider)
    {
        NameTextBox.Text = provider.Name;
        DescriptionTextBox.Text = provider.Description ?? string.Empty;
        
        // Set type
        TypeComboBox.SelectedIndex = provider.Type == DnsProviderType.DoH ? 1 : 0;

        // Standard DNS fields
        PrimaryIpv4TextBox.Text = provider.PrimaryIpv4 ?? string.Empty;
        SecondaryIpv4TextBox.Text = provider.SecondaryIpv4 ?? string.Empty;
        PrimaryIpv6TextBox.Text = provider.PrimaryIpv6 ?? string.Empty;
        SecondaryIpv6TextBox.Text = provider.SecondaryIpv6 ?? string.Empty;

        // DoH fields
        DohUrlTextBox.Text = provider.DohUrl ?? string.Empty;
        BootstrapIpsTextBox.Text = string.Join(Environment.NewLine, provider.BootstrapIps);

        UpdateTypeVisibility();
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTypeVisibility();
    }

    private void UpdateTypeVisibility()
    {
        if (StandardDnsPanel is null || DohPanel is null) return;

        var isDoH = TypeComboBox.SelectedIndex == 1;
        StandardDnsPanel.Visibility = isDoH ? Visibility.Collapsed : Visibility.Visible;
        DohPanel.Visibility = isDoH ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TryGetProvider(out var provider, out var errorMessage) && provider is not null)
        {
            ResultProvider = provider;
            Confirmed = true;
            Close();
        }
        else
        {
            // Show validation error
            System.Windows.MessageBox.Show(
                errorMessage ?? Loc.Get("ProviderDialog_ValidationError"),
                Loc.Get("ProviderDialog_ValidationTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    /// <summary>
    /// Validates and returns the provider data.
    /// </summary>
    public bool TryGetProvider(out DnsProvider? provider, out string? validationError)
    {
        provider = null;
        validationError = null;

        var errors = new List<string>();

        // Validate name
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            errors.Add(Loc.Get("ProviderDialog_ErrorNameRequired"));
        }

        var isDoH = TypeComboBox.SelectedIndex == 1;

        if (isDoH)
        {
            // Validate DoH URL
            if (string.IsNullOrWhiteSpace(DohUrlTextBox.Text))
            {
                errors.Add(Loc.Get("ProviderDialog_ErrorDohUrlRequired"));
            }
            else if (!Uri.TryCreate(DohUrlTextBox.Text.Trim(), UriKind.Absolute, out var dohUri) ||
                     !string.Equals(dohUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Loc.Get("ProviderDialog_ErrorDohUrlInvalid"));
            }
        }
        else
        {
            // Validate at least primary IPv4
            if (string.IsNullOrWhiteSpace(PrimaryIpv4TextBox.Text))
            {
                errors.Add(Loc.Get("ProviderDialog_ErrorPrimaryIpv4Required"));
            }
            else if (!IPAddress.TryParse(PrimaryIpv4TextBox.Text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                errors.Add(Loc.Get("ProviderDialog_ErrorPrimaryIpv4Invalid"));
            }
        }

        // Validate optional IPs
        void ValidateOptionalIp(string? text, bool ipv6, string errorKey)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!IPAddress.TryParse(text.Trim(), out var parsed))
            {
                errors.Add(Loc.Get(errorKey));
                return;
            }

            if (ipv6 && parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                errors.Add(Loc.Get(errorKey));
            }

            if (!ipv6 && parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                errors.Add(Loc.Get(errorKey));
            }
        }

        ValidateOptionalIp(SecondaryIpv4TextBox.Text, ipv6: false, "ProviderDialog_ErrorSecondaryIpv4Invalid");
        ValidateOptionalIp(PrimaryIpv6TextBox.Text, ipv6: true, "ProviderDialog_ErrorPrimaryIpv6Invalid");
        ValidateOptionalIp(SecondaryIpv6TextBox.Text, ipv6: true, "ProviderDialog_ErrorSecondaryIpv6Invalid");

        // Validate bootstrap IPs (optional)
        var bootstrapIps = new List<string>();
        if (!string.IsNullOrWhiteSpace(BootstrapIpsTextBox.Text))
        {
            var lines = BootstrapIpsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));

            foreach (var line in lines)
            {
                if (!IPAddress.TryParse(line, out _))
                {
                    errors.Add(Loc.GetFormat("ProviderDialog_ErrorBootstrapIpInvalid", line));
                }
                else
                {
                    bootstrapIps.Add(line);
                }
            }
        }

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.Get("ProviderDialog_ValidationError"));
            foreach (var err in errors.Distinct())
            {
                sb.Append("- ").AppendLine(err);
            }
            validationError = sb.ToString().TrimEnd();
            return false;
        }

        provider = new DnsProvider
        {
            Id = _provider?.Id ?? Guid.NewGuid(),
            Name = NameTextBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim(),
            Type = isDoH ? DnsProviderType.DoH : DnsProviderType.Standard,
            PrimaryIpv4 = string.IsNullOrWhiteSpace(PrimaryIpv4TextBox.Text) ? null : PrimaryIpv4TextBox.Text.Trim(),
            SecondaryIpv4 = string.IsNullOrWhiteSpace(SecondaryIpv4TextBox.Text) ? null : SecondaryIpv4TextBox.Text.Trim(),
            PrimaryIpv6 = string.IsNullOrWhiteSpace(PrimaryIpv6TextBox.Text) ? null : PrimaryIpv6TextBox.Text.Trim(),
            SecondaryIpv6 = string.IsNullOrWhiteSpace(SecondaryIpv6TextBox.Text) ? null : SecondaryIpv6TextBox.Text.Trim(),
            DohUrl = string.IsNullOrWhiteSpace(DohUrlTextBox.Text) ? null : DohUrlTextBox.Text.Trim(),
            BootstrapIps = bootstrapIps,
            IsBuiltIn = false
        };

        ResultProvider = provider;
        return true;
    }

    public bool IsNewProvider => _isNew;
}
