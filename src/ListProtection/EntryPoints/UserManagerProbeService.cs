using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Users;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ListProtection
{
    public class UserManagerProbeService : IServerEntryPoint
    {
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public UserManagerProbeService(IUserManager userManager, ILogManager logManager)
        {
            _userManager = userManager;
            _logger = logManager.GetLogger("ListProtection.UserManagerProbe");
        }

        public void Run()
        {
            try
            {
                var users = _userManager.GetUserList(new UserQuery());
                _logger.Info("[UserManagerProbe] GetUserList returned {0} user(s)", users.Length);

                foreach (var user in users)
                {
                    var policy = _userManager.GetUserPolicy(user);
                    _logger.Info(
                        "[UserManagerProbe] User | Name={0} | Id={1} | InternalId={2} | IsAdmin={3}",
                        user.Name,
                        user.Id,
                        user.InternalId,
                        policy?.IsAdministrator
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[UserManagerProbe] Exception: {0}", ex);
            }
        }

        public void Dispose() { }
    }
}