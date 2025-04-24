namespace BingAdsWebApp.Services
{
    public class TokenService
    {
        private readonly IConfiguration _configuration;
        private readonly string _tokenFilePath;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "BingAdsWebApp");
            Directory.CreateDirectory(appFolder);

            _tokenFilePath = Path.Combine(appFolder, "refresh_token.txt");
        }

        /// <summary>
        /// Saves the refresh token
        /// </summary>
        /// <param name="refreshToken">The refresh token to save</param>
        public void SaveRefreshToken(string refreshToken)
        {
            File.WriteAllText(_tokenFilePath, refreshToken);
        }

        /// <summary>
        /// Deletes the contents in the refresh token file
        /// </summary>
        public void DeleteRefreshToken()
        {
            File.WriteAllText(_tokenFilePath, "");
        }

        /// <summary>
        /// Determines whether the global refresh token exists.
        /// </summary>
        /// <returns>Returns true if the global refresh token exists.</returns>
        public bool RefreshTokenExists()
        {
            return File.Exists(_tokenFilePath) && File.ReadAllText(_tokenFilePath).Length > 0;
        }

        /// <summary>
        /// Gets the global refresh token.
        /// </summary>
        /// <returns>The global refresh token.</returns>
        public string GetRefreshToken()
        {
            return File.Exists(_tokenFilePath) ? File.ReadAllText(_tokenFilePath) : string.Empty;
        }
    }
}
