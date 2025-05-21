using BingAdsDemo.BingAdsExampleLibrary.v13;
using Microsoft.BingAds.V13.CustomerManagement;
using Microsoft.BingAds;
using System.ServiceModel;
using Microsoft.BingAds.V13.CampaignManagement;

namespace BIngAdsDemo.BingAds
{
    public class SearchCompanies : ExampleBase
    {
        public override string Description
        {
            get { return "Search Companies by Name | Customer Management V13"; }
        }

        // The language and locale of the  profile data file available for download.
        // This example uses 'en' (English). Supported locales are 'zh-Hant' (Traditional Chinese), 'en' (English), 'fr' (French), 
        // 'de' (German), 'it' (Italian), 'pt-BR' (Portuguese - Brazil), and 'es' (Spanish). 

        private const string LanguageLocale = "en";

        private const string companyNameFilter = "impartner";

        public async override Task RunAsync(AuthorizationData authorizationData)
        {
            try
            {
                ApiEnvironment environment = ((OAuthWebAuthCodeGrant)authorizationData.Authentication).Environment;

                CampaignManagementExampleHelper CampaignManagementExampleHelper = new CampaignManagementExampleHelper(
                    OutputStatusMessageDefault: this.OutputStatusMessage);
                CampaignManagementExampleHelper.CampaignManagementService = new ServiceClient<ICampaignManagementService>(
                    authorizationData: authorizationData,
                    environment: environment);

                OutputStatusMessage("-----\nSearchCompanies:");
                var companies = (await CampaignManagementExampleHelper.SearchCompaniesAsync(
                    companyNameFilter: companyNameFilter,
                    languageLocale: LanguageLocale)).Companies.ToArray();

                OutputStatusMessage("Companies:");
                CampaignManagementExampleHelper.OutputArrayOfCompanies(companies);
            }
            // Catch authentication exceptions
            catch (OAuthTokenRequestException ex)
            {
                OutputStatusMessage(string.Format("Couldn't get OAuth tokens. Error: {0}. Description: {1}", ex.Details.Error, ex.Details.Description));
            }
            // Catch Customer Management service exceptions
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.AdApiFaultDetail> ex)
            {
                OutputStatusMessage(string.Join("; ", ex.Detail.Errors.Select(error => string.Format("{0}: {1}", error.Code, error.Message))));
            }
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.ApiFault> ex)
            {
                OutputStatusMessage(string.Join("; ", ex.Detail.OperationErrors.Select(error => string.Format("{0}: {1}", error.Code, error.Message))));
            }
            catch (Exception ex)
            {
                OutputStatusMessage(ex.Message);
            }
        }
    }
}
