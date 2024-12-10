namespace CASInstaller;

public partial class Product
{
    public class InstallSettings
    {
        public bool CreateBuildInfo { get; set; } = true;
        public bool CreatePatchResult { get; set; } = true;
        public bool CreateProductDB { get; set; } = true;
        public bool CreateLauncherDB { get; set; } = true;
        public bool CreateFlavorInfo { get; set; } = true;
    }
}