using System.Globalization;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentUiTextLocalizerTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Theory]
    [InlineData("Applying Windows drivers: 17,6%", "Application des pilotes Windows : 17,6 %")]
    [InlineData("Mounting WinRE: 25%", "Montage de WinRE : 25 %")]
    [InlineData("Applying WinRE drivers: 50%", "Application des pilotes WinRE : 50 %")]
    [InlineData("Unmounting WinRE: 75%", "Démontage de WinRE : 75 %")]
    [InlineData("Downloading: 10%", "Téléchargement : 10 %")]
    [InlineData("Extracting: 25%", "Extraction : 25 %")]
    [InlineData("Applying: 50%", "Application : 50 %")]
    [InlineData("Staging: 75%", "Préparation : 75 %")]
    public void LocalizeMessage_TranslatesGeneratedStepPercentLabels(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, DeploymentUiTextLocalizer.LocalizeMessage(input));
    }

    [Theory]
    [InlineData("Resolving cache location...", "Résolution de l’emplacement du cache...")]
    [InlineData("Cache strategy resolved.", "Stratégie de cache résolue.")]
    [InlineData("Cache strategy resolved (simulation).", "Stratégie de cache résolue (simulation).")]
    [InlineData("Operating system image downloaded.", "Image système téléchargée.")]
    [InlineData("Operating system image resolved from cache.", "Image système réutilisée depuis le cache.")]
    [InlineData("Driver pack resolved from cache.", "Pack de pilotes réutilisé depuis le cache.")]
    [InlineData("Driver pack prepared for deferred installation.", "Pack de pilotes préparé pour l’installation différée.")]
    [InlineData("Driver pack source payload is unavailable for deferred staging.", "La charge utile source du pack de pilotes est indisponible pour la préparation différée.")]
    [InlineData("Microsoft Update Catalog did not produce a driver payload.", "Microsoft Update Catalog n’a produit aucune charge utile de pilote.")]
    [InlineData("Deferred driver pack staging was requested without a supported deferred command.", "La préparation différée du pack de pilotes a été demandée sans commande différée prise en charge.")]
    [InlineData("Configuring OOBE settings...", "Configuration des paramètres OOBE...")]
    [InlineData("Writing first-run privacy defaults...", "Écriture des valeurs de confidentialité du premier démarrage...")]
    [InlineData("OOBE customization disabled.", "Personnalisation OOBE désactivée.")]
    [InlineData("OOBE settings configured.", "Paramètres OOBE configurés.")]
    [InlineData("OOBE settings configured (simulation).", "Paramètres OOBE configurés (simulation).")]
    [InlineData("Downloading Driver pack...", "Téléchargement du pack de pilotes...")]
    [InlineData("System reboot", "Redémarrage système")]
    [InlineData("Required reboot executable 'wpeutil.exe' was not found.", "L’exécutable de redémarrage requis 'wpeutil.exe' est introuvable.")]
    public void LocalizeMessage_TranslatesDeploymentRuntimeMessages(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, DeploymentUiTextLocalizer.LocalizeMessage(input));
    }

    [Fact]
    public void LocalizeStepName_TranslatesOobeStepName()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal("Configuration des paramètres OOBE", DeploymentUiTextLocalizer.LocalizeStepName("Configure OOBE settings"));
    }

    [Theory]
    [InlineData("Target disk 3 is no longer present.", "Le disque cible 3 n’est plus présent.")]
    [InlineData("Target disk 3 is blocked: Blocked: system disk", "Le disque cible 3 est bloqué : Bloqué : disque système")]
    [InlineData("12.5 MB downloaded", "12.5 MB téléchargés")]
    public void LocalizeMessage_TranslatesDynamicDeploymentRuntimeMessages(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, DeploymentUiTextLocalizer.LocalizeMessage(input));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}
