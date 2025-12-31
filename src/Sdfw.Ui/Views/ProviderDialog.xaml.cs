using System.Net;
using System.Text;
using System.Windows;
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
        DohUrlTextBox.Text = string.Empty;
        PrimaryIpv4TextBox.Text = string.Empty;
        SecondaryIpv4TextBox.Text = string.Empty;
        PrimaryIpv6TextBox.Text = string.Empty;
        SecondaryIpv6TextBox.Text = string.Empty;
    }

    private void PopulateFields(DnsProvider provider)
    {
        NameTextBox.Text = provider.Name;
        DescriptionTextBox.Text = provider.Description ?? string.Empty;
        DohUrlTextBox.Text = provider.DohUrl ?? string.Empty;
        PrimaryIpv4TextBox.Text = provider.PrimaryIpv4 ?? string.Empty;
        SecondaryIpv4TextBox.Text = provider.SecondaryIpv4 ?? string.Empty;
        PrimaryIpv6TextBox.Text = provider.PrimaryIpv6 ?? string.Empty;
        SecondaryIpv6TextBox.Text = provider.SecondaryIpv6 ?? string.Empty;
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
    /// DoH is primary when DoH URL is filled; otherwise Standard DNS is used.
    /// </summary>
    public bool TryGetProvider(out DnsProvider? provider, out string? validationError)
    {
        provider = null;
        validationError = null;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            errors.Add(Loc.Get("ProviderDialog_ErrorNameRequired"));
        }

        var hasDoHUrl = !string.IsNullOrWhiteSpace(DohUrlTextBox.Text);
        var hasPrimaryIpv4 = !string.IsNullOrWhiteSpace(PrimaryIpv4TextBox.Text);

        if (hasDoHUrl)
        {
            if (!Uri.TryCreate(DohUrlTextBox.Text.Trim(), UriKind.Absolute, out var dohUri) ||
                !string.Equals(dohUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Loc.Get("ProviderDialog_ErrorDohUrlInvalid"));
            }
        }

        if (!hasDoHUrl && !hasPrimaryIpv4)
        {
            errors.Add(Loc.Get("ProviderDialog_ErrorPrimaryIpv4Required"));
        }

        if (hasPrimaryIpv4)
        {
            if (!IPAddress.TryParse(PrimaryIpv4TextBox.Text.Trim(), out var ip) ||
                ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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

        var providerType = hasDoHUrl ? DnsProviderType.DoH : DnsProviderType.Standard;

        provider = new DnsProvider
        {
            Id = _provider?.Id ?? Guid.NewGuid(),
            Name = NameTextBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim(),
            Type = providerType,
            PrimaryIpv4 = string.IsNullOrWhiteSpace(PrimaryIpv4TextBox.Text) ? null : PrimaryIpv4TextBox.Text.Trim(),
            SecondaryIpv4 = string.IsNullOrWhiteSpace(SecondaryIpv4TextBox.Text) ? null : SecondaryIpv4TextBox.Text.Trim(),
            PrimaryIpv6 = string.IsNullOrWhiteSpace(PrimaryIpv6TextBox.Text) ? null : PrimaryIpv6TextBox.Text.Trim(),
            SecondaryIpv6 = string.IsNullOrWhiteSpace(SecondaryIpv6TextBox.Text) ? null : SecondaryIpv6TextBox.Text.Trim(),
            DohUrl = string.IsNullOrWhiteSpace(DohUrlTextBox.Text) ? null : DohUrlTextBox.Text.Trim(),
            BootstrapIps = [], // Bootstrap IPs removed
            IsBuiltIn = false
        };

        ResultProvider = provider;
        return true;
    }

    public bool IsNewProvider => _isNew;
}
