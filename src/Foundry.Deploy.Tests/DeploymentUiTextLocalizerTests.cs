// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
    [InlineData("Operating system image downloaded.", "Image du système d’exploitation téléchargée.")]
    [InlineData("Operating system image resolved from cache.", "Image du système d’exploitation réutilisée depuis le cache.")]
    [InlineData("Driver pack resolved from cache.", "Pack de pilotes réutilisé depuis le cache.")]
    [InlineData("Driver pack prepared for deferred installation.", "Pack de pilotes préparé pour l’installation au premier démarrage.")]
    [InlineData("Driver pack source payload is unavailable for deferred staging.", "Le contenu source du pack de pilotes est indisponible pour la préparation au premier démarrage.")]
    [InlineData("Microsoft Update Catalog did not produce a driver payload.", "Microsoft Update Catalog n’a produit aucun contenu de pilote.")]
    [InlineData("Deferred driver pack staging was requested without a supported deferred command.", "La préparation du pack de pilotes au premier démarrage a été demandée sans commande prise en charge.")]
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
    [InlineData("Pre-OOBE customizations will be staged with the deferred driver pack.", "Les personnalisations pré-OOBE seront préparées avec le pack de pilotes au premier démarrage.")]
    [InlineData("Downloading Driver pack...", "Téléchargement du pack de pilotes...")]
    [InlineData("Capturing Autopilot hardware hash...", "Capture du hash matériel Autopilot...")]
    [InlineData("Running OA3Tool...", "Exécution de OA3Tool...")]
    [InlineData("Uploading Autopilot hardware hash...", "Envoi du hash matériel Autopilot...")]
    [InlineData("Preparing Microsoft Graph import...", "Préparation de l’import Microsoft Graph...")]
    [InlineData("Submitting import request to Microsoft Graph...", "Envoi de la demande d’import à Microsoft Graph...")]
    [InlineData("Waiting for Autopilot device visibility...", "Attente de la visibilité de l’appareil Autopilot...")]
    [InlineData("Updating existing Autopilot device...", "Mise à jour de l’appareil Autopilot existant...")]
    [InlineData("Updating Windows Autopilot group tag in Microsoft Graph...", "Mise à jour du group tag Windows Autopilot dans Microsoft Graph...")]
    [InlineData("Waiting for Autopilot group tag update...", "Attente de la mise à jour du group tag Autopilot...")]
    [InlineData("Preparing Autopilot hardware hash upload...", "Préparation de l’envoi du hash matériel Autopilot...")]
    [InlineData("Decrypting media certificate...", "Déchiffrement du certificat du média...")]
    [InlineData("Authenticating Autopilot hardware hash upload...", "Authentification de l’envoi du hash matériel Autopilot...")]
    [InlineData("Requesting Microsoft Graph token...", "Demande du jeton Microsoft Graph...")]
    [InlineData("Importing hardware hash into Microsoft Graph...", "Import du hash matériel dans Microsoft Graph...")]
    [InlineData("Writing dry-run Autopilot hash manifest...", "Écriture du manifeste Autopilot simulé...")]
    [InlineData("Target Windows partition is unavailable for Autopilot hardware hash upload.", "La partition Windows cible est indisponible pour l’envoi du hash matériel Autopilot.")]
    [InlineData("Autopilot hardware hash upload skipped because the embedded certificate is expired.", "Envoi du hash matériel Autopilot ignoré car le certificat intégré a expiré.")]
    [InlineData("Autopilot hardware hash upload skipped because media metadata is incomplete.", "Envoi du hash matériel Autopilot ignoré car les informations de certificat du média sont incomplètes.")]
    [InlineData("Autopilot hardware hash imported and visible in Windows Autopilot devices.", "Hash matériel Autopilot importé et visible dans les appareils Windows Autopilot.")]
    [InlineData("Imported Autopilot device did not appear in Windows Autopilot devices before the timeout.", "L’appareil Autopilot importé n’est pas apparu dans les appareils Windows Autopilot avant l’expiration du délai.")]
    [InlineData("Windows Autopilot device group tag update was not confirmed before the timeout.", "La mise à jour du group tag de l’appareil Windows Autopilot n’a pas été confirmée avant l’expiration du délai.")]
    [InlineData("Autopilot hardware hash upload prepared for dry run.", "Envoi du hash matériel Autopilot préparé pour la simulation.")]
    [InlineData("Autopilot hardware hash upload prepared (simulation).", "Envoi du hash matériel Autopilot préparé (simulation).")]
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
    [InlineData("Checking Windows Autopilot devices (1 second remaining)...", "Vérification des appareils Windows Autopilot (1s restant)...")]
    [InlineData("Checking Windows Autopilot devices (600 seconds remaining)...", "Vérification des appareils Windows Autopilot (600s restant)...")]
    [InlineData("Checking Windows Autopilot group tag (600 seconds remaining)...", "Vérification du group tag Windows Autopilot (600s restant)...")]
    [InlineData("Selected Autopilot profile file was not found: 'X:\\profile.json'.", "Le fichier du profil Autopilot sélectionné est introuvable : 'X:\\profile.json'.")]
    [InlineData("Autopilot hardware hash capture failed: OA3 report does not contain a serial number.", "La capture du hash matériel Autopilot a échoué : OA3 report does not contain a serial number.")]
    [InlineData("Autopilot hardware hash import failed: ZtdDeviceAlreadyAssigned.", "L’import du hash matériel Autopilot a échoué : ZtdDeviceAlreadyAssigned.")]
    [InlineData("Autopilot hardware hash upload skipped: Permission denied.", "Envoi du hash matériel Autopilot ignoré : Permission denied.")]
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
