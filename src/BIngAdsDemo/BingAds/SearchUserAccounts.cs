using Microsoft.BingAds.V13.CustomerManagement;
using Microsoft.BingAds;
using System.ServiceModel;
using BingAdsDemo.BingAdsExampleLibrary.v13;

namespace BIngAdsDemo.BingAds
{
    /// <summary>
    /// How to search for accounts that can be managed by the current authenticated user.
    /// </summary>
    public class SearchUserAccounts : ExampleBase
    {
        public override string Description
        {
            get { return "Search Accounts for Current User | Customer Management V13"; }
        }

        public async override Task RunAsync(AuthorizationData authorizationData)
        {
            try
            {
                ApiEnvironment environment = ((OAuthWebAuthCodeGrant)authorizationData.Authentication).Environment;

                CustomerManagementExampleHelper customerManagementExampleHelper = new CustomerManagementExampleHelper(
                    OutputStatusMessageDefault: this.OutputStatusMessage);
                customerManagementExampleHelper.CustomerManagementService = new ServiceClient<ICustomerManagementService>(
                    authorizationData: authorizationData,
                    environment: environment);

                OutputStatusMessage("-----\nGetUser:");
                var getUserResponse = await customerManagementExampleHelper.GetUserAsync(
                    userId: null);
                var user = getUserResponse.User;
                OutputStatusMessage("User:");
                customerManagementExampleHelper.OutputUser(user);
                OutputStatusMessage("CustomerRoles:");
                customerManagementExampleHelper.OutputArrayOfCustomerRole(getUserResponse.CustomerRoles);

                // Search for the accounts that the user can access.
                // To retrieve more than 100 accounts, increase the page size up to 1,000.
                // To retrieve more than 1,000 accounts you'll need to add paging.

                var predicate = new Predicate
                {
                    Field = "UserId",
                    Operator = PredicateOperator.Equals,
                    Value = user.Id.ToString()
                };

                var paging = new Paging
                {
                    Index = 0,
                    Size = 100
                };

                OutputStatusMessage("-----\nSearchAccounts:");
                var accounts = (await customerManagementExampleHelper.SearchAccountsAsync(
                    predicates: new[] { predicate },
                    ordering: null,
                    pageInfo: paging,
                    null))?.Accounts.ToArray();
                OutputStatusMessage("Accounts:");
                customerManagementExampleHelper.OutputArrayOfAdvertiserAccount(accounts);

                HashSet<long> distinctCustomerIds = new HashSet<long>();
                foreach (var account in accounts)
                {
                    distinctCustomerIds.Add(account.ParentCustomerId);
                }

                foreach (var customerId in distinctCustomerIds)
                {
                    // You can find out which pilot features the customer is able to use. 
                    // Each account could belong to a different customer, so use the customer ID in each account.
                    OutputStatusMessage("-----\nGetCustomerPilotFeatures:");
                    OutputStatusMessage(string.Format("Requested by CustomerId: {0}", customerId));
                    var featurePilotFlags = (await customerManagementExampleHelper.GetCustomerPilotFeaturesAsync(
                        customerId: customerId)).FeaturePilotFlags;
                    OutputStatusMessage("Customer Pilot flags:");
                    OutputStatusMessage(string.Join("; ", featurePilotFlags.Select(flag => string.Format("{0}", flag))));
                }
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
