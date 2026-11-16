namespace MagneticDesk.Services;

public static class AppUpdateServiceFactory
{
    public static IAppUpdateService Create(AppDistributionService distribution)
    {
        return new DisabledAppUpdateService();
    }
}
