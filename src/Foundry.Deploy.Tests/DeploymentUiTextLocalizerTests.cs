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
    [InlineData("Offline customization disabled.", "Personnalisation hors ligne désactivée.")]
    [InlineData("Configuring offline customizations...", "Configuration des personnalisations hors ligne...")]
    [InlineData("Writing first-run defaults...", "Écriture des valeurs du premier démarrage...")]
    [InlineData("Configuring AI component removal...", "Configuration de la suppression des composants IA...")]
    [InlineData("Writing offline AI policies...", "Écriture des stratégies IA hors ligne...")]
    [InlineData("Offline customization configured.", "Personnalisation hors ligne configurée.")]
    [InlineData("Offline customization configured (simulation).", "Personnalisation hors ligne configurée (simulation).")]
    [InlineData("Configuring OOBE settings...", "Configuration des paramètres OOBE...")]
    [InlineData("Writing first-run privacy defaults...", "Écriture des valeurs de confidentialité du premier démarrage...")]
    [InlineData("OOBE customization disabled.", "Personnalisation OOBE désactivée.")]
    [InlineData("OOBE settings configured.", "Paramètres OOBE configurés.")]
    [InlineData("OOBE settings configured (simulation).", "Paramètres OOBE configurés (simulation).")]
    [InlineData("Staging pre-OOBE customizations...", "Préparation des personnalisations pré-OOBE...")]
    [InlineData("No pre-OOBE customization scripts are required.", "Aucun script de personnalisation pré-OOBE n’est requis.")]
    [InlineData("Pre-OOBE customizations staged.", "Personnalisations pré-OOBE préparées.")]
    [InlineData("Pre-OOBE customizations staged (simulation).", "Personnalisations pré-OOBE préparées (simulation).")]
    [InlineData("Pre-OOBE customizations will be staged with the deferred driver pack.", "Les personnalisations pré-OOBE seront préparées avec le pack de pilotes différé.")]
    [InlineData("Downloading Driver pack...", "Téléchargement du pack de pilotes...")]
    [InlineData("Capturing Autopilot hardware hash...", "Capture du hardware hash Autopilot...")]
    [InlineData("Running OA3Tool...", "Exécution de OA3Tool...")]
    [InlineData("Uploading Autopilot hardware hash...", "Upload du hardware hash Autopilot...")]
    [InlineData("Preparing Microsoft Graph import...", "Préparation de l’import Microsoft Graph...")]
    [InlineData("Submitting import request to Microsoft Graph...", "Envoi de la demande d’import à Microsoft Graph...")]
    [InlineData("Waiting for Autopilot import...", "Attente de l’import Autopilot...")]
    [InlineData("Waiting for Autopilot device visibility...", "Attente de la visibilité de l’appareil Autopilot...")]
    [InlineData("Preparing Autopilot hardware hash upload...", "Préparation de l’upload du hardware hash Autopilot...")]
    [InlineData("Writing dry-run Autopilot hash manifest...", "Écriture du manifeste de simulation de l’upload du hardware hash Autopilot...")]
    [InlineData("System reboot", "Redémarrage système")]
    [InlineData("Required reboot executable 'wpeutil.exe' was not found.", "L’exécutable de redémarrage requis 'wpeutil.exe' est introuvable.")]
    public void LocalizeMessage_TranslatesDeploymentRuntimeMessages(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, DeploymentUiTextLocalizer.LocalizeMessage(input));
    }

    [Fact]
    public void LocalizeStepName_TranslatesProvisionAutopilotStepName()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal("Préparation d’Autopilot", DeploymentUiTextLocalizer.LocalizeStepName("Provision Autopilot"));
    }

    [Theory]
    [InlineData("Target disk 3 is no longer present.", "Le disque cible 3 n’est plus présent.")]
    [InlineData("Target disk 3 is blocked: Blocked: system disk", "Le disque cible 3 est bloqué : Bloqué : disque système")]
    [InlineData("12.5 MB downloaded", "12.5 MB téléchargés")]
    [InlineData("Waiting for Microsoft Graph import completion (10 minutes remaining)...", "Attente de la fin de l’import Microsoft Graph (10 minutes restantes)...")]
    [InlineData("Waiting for device to appear in Windows Autopilot devices (42 seconds remaining)...", "Attente de l’apparition de l’appareil dans Windows Autopilot devices (42 secondes restantes)...")]
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
