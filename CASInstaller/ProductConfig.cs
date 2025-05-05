using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace CASInstaller;

public class ProductConfig
{
    public All all { get; set; }
    public Cn cn { get; set; }
    public Dede dede { get; set; }
    public Enus enus { get; set; }
    public Eses eses { get; set; }
    public Esmx esmx { get; set; }
    public Frfr frfr { get; set; }
    public Kokr kokr { get; set; }
    public Platform platform { get; set; }
    public Ptbr ptbr { get; set; }
    public Ruru ruru { get; set; }
    public Zhcn zhcn { get; set; }
    public Zhtw zhtw { get; set; }

    public static async Task<ProductConfig?> GetProductConfig(CDN cdn, Hash key)
    {
        if (cdn.Hosts == null || key.IsEmpty()) return null;

        var hosts = cdn.Hosts;
        if (hosts == null) return null;

        var data = await cdn.GetCDNConfig(key);

        var options = new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        var reader = new Utf8JsonReader(data, options);
        return JsonSerializer.Deserialize<ProductConfig>(ref reader);
    }

    public void Dump(string productconfigJson)
    {
        var reserialized = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(productconfigJson, reserialized);
    }

    public class All
    {
        public Config config { get; set; }
    }

    public class Config
    {
        public string data_dir { get; set; }
        public string decryption_key_name { get; set; }
        public string[] display_locales { get; set; }
        public bool enable_block_copy_patch { get; set; }
        public Form form { get; set; }
        public string[] launch_arguments { get; set; }
        public Launcher_Install_Info launcher_install_info { get; set; }
        public Opaque_Product_Specific opaque_product_specific { get; set; }
        public string product { get; set; }
        public string shared_container_default_subfolder { get; set; }
        public string[] supported_locales { get; set; }
        public bool supports_multibox { get; set; }
        public bool supports_offline { get; set; }
        public Title_Info title_info { get; set; }
        public string update_method { get; set; }
    }

    public class Form
    {
        public Game_Dir game_dir { get; set; }
    }

    public class Game_Dir
    {
        public string dirname { get; set; }
    }

    public class Launcher_Install_Info
    {
        public string bootstrapper_branch { get; set; }
        public string bootstrapper_product { get; set; }
        public string product_tag { get; set; }
    }

    public class Opaque_Product_Specific
    {
        public string uses_web_credentials { get; set; }
    }

    public class Title_Info
    {
        public string title_id { get; set; }
    }

    public class Cn
    {
        public Config1 config { get; set; }
    }

    public class Config1
    {
        public string[] display_locales { get; set; }
        public string[] extra_tags { get; set; }
    }

    public class Dede
    {
        public Config2 config { get; set; }
    }

    public class Config2
    {
        public Install[] install { get; set; }
    }

    public class Install
    {
        public Start_Menu_Shortcut start_menu_shortcut { get; set; }
        public Desktop_Shortcut desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Enus
    {
        public Config3 config { get; set; }
    }

    public class Config3
    {
        public Install1[] install { get; set; }
    }

    public class Install1
    {
        public Start_Menu_Shortcut1 start_menu_shortcut { get; set; }
        public Desktop_Shortcut1 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key1 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut1
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut1
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key1
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Eses
    {
        public Config4 config { get; set; }
    }

    public class Config4
    {
        public Install2[] install { get; set; }
    }

    public class Install2
    {
        public Start_Menu_Shortcut2 start_menu_shortcut { get; set; }
        public Desktop_Shortcut2 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key2 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut2
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut2
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key2
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Esmx
    {
        public Config5 config { get; set; }
    }

    public class Config5
    {
        public Install3[] install { get; set; }
    }

    public class Install3
    {
        public Start_Menu_Shortcut3 start_menu_shortcut { get; set; }
        public Desktop_Shortcut3 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key3 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut3
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut3
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key3
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Frfr
    {
        public Config6 config { get; set; }
    }

    public class Config6
    {
        public Install4[] install { get; set; }
    }

    public class Install4
    {
        public Start_Menu_Shortcut4 start_menu_shortcut { get; set; }
        public Desktop_Shortcut4 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key4 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut4
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut4
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key4
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Kokr
    {
        public Config7 config { get; set; }
    }

    public class Config7
    {
        public Install5[] install { get; set; }
    }

    public class Install5
    {
        public Start_Menu_Shortcut5 start_menu_shortcut { get; set; }
        public Desktop_Shortcut5 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key5 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut5
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut5
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key5
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Platform
    {
        public Mac mac { get; set; }
        public Win win { get; set; }
    }

    public class Mac
    {
        public Config8 config { get; set; }
    }

    public class Config8
    {
        public Binaries binaries { get; set; }
        public Form1 form { get; set; }
        public Min_Spec min_spec { get; set; }
        public string[] shared_container_delete_list { get; set; }
        public string[] shared_container_move_list { get; set; }
        public string shortcut_target_path { get; set; }
        public string[] tags { get; set; }
        public string[] tags_32bit { get; set; }
        public string[] tags_64bit { get; set; }
        public Uninstall[] uninstall { get; set; }
    }

    public class Binaries
    {
        public Game game { get; set; }
    }

    public class Game
    {
        public object[] launch_arguments { get; set; }
        public string relative_path { get; set; }
        public bool switcher { get; set; }
    }

    public class Form1
    {
        public Game_Dir1 game_dir { get; set; }
    }

    public class Game_Dir1
    {
        public string _default { get; set; }
        public long required_space { get; set; }
        public int space_per_extra_language { get; set; }
    }

    public class Min_Spec
    {
        public int default_required_cpu_cores { get; set; }
        public int default_required_cpu_speed { get; set; }
        public int default_required_ram { get; set; }
        public bool default_requires_64_bit { get; set; }
        public Required_Osspecs required_osspecs { get; set; }
    }

    public class Required_Osspecs
    {
        public _1011 _1011 { get; set; }
    }

    public class _1011
    {
        public int required_subversion { get; set; }
    }

    public class Uninstall
    {
        public Delete_Folder delete_folder { get; set; }
        public Delete_File delete_file { get; set; }
    }

    public class Delete_Folder
    {
        public string[] relative_paths { get; set; }
        public string root { get; set; }
    }

    public class Delete_File
    {
        public string[] relative_paths { get; set; }
        public string root { get; set; }
    }

    public class Win
    {
        public Config9 config { get; set; }
    }

    public class Config9
    {
        public Binaries1 binaries { get; set; }
        public Form2 form { get; set; }
        public Min_Spec1 min_spec { get; set; }
        public string[] shared_container_delete_list { get; set; }
        public string[] shared_container_move_list { get; set; }
        public string shortcut_target_path { get; set; }
        public string[] tags { get; set; }
        public string[] tags_32bit { get; set; }
        public string[] tags_64bit { get; set; }
        public string[] tags_arm64 { get; set; }
        public Uninstall1[] uninstall { get; set; }
    }

    public class Binaries1
    {
        public Game1 game { get; set; }
    }

    public class Game1
    {
        public object[] launch_arguments { get; set; }
        public string relative_path { get; set; }
        public string relative_path_arm64 { get; set; }
        public bool switcher { get; set; }
    }

    public class Form2
    {
        public Game_Dir2 game_dir { get; set; }
    }

    public class Game_Dir2
    {
        public string _default { get; set; }
        public long required_space { get; set; }
        public int space_per_extra_language { get; set; }
    }

    public class Min_Spec1
    {
        public int default_required_cpu_cores { get; set; }
        public int default_required_cpu_speed { get; set; }
        public int default_required_ram { get; set; }
        public bool default_requires_64_bit { get; set; }
        public Required_Osspecs1 required_osspecs { get; set; }
    }

    public class Required_Osspecs1
    {
        public _61 _61 { get; set; }
    }

    public class _61
    {
        public int required_subversion { get; set; }
    }

    public class Uninstall1
    {
        public Delete_Registry_Key_List delete_registry_key_list { get; set; }
        public Delete_Folder1 delete_folder { get; set; }
        public Delete_File1 delete_file { get; set; }
    }

    public class Delete_Registry_Key_List
    {
        public string flags { get; set; }
        public string key_type { get; set; }
        public string root { get; set; }
        public string[] subkeys { get; set; }
    }

    public class Delete_Folder1
    {
        public string[] relative_paths { get; set; }
        public string root { get; set; }
    }

    public class Delete_File1
    {
        public string[] relative_paths { get; set; }
        public string root { get; set; }
    }

    public class Ptbr
    {
        public Config10 config { get; set; }
    }

    public class Config10
    {
        public Install6[] install { get; set; }
    }

    public class Install6
    {
        public Start_Menu_Shortcut6 start_menu_shortcut { get; set; }
        public Desktop_Shortcut6 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key6 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut6
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut6
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key6
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Ruru
    {
        public Config11 config { get; set; }
    }

    public class Config11
    {
        public Install7[] install { get; set; }
    }

    public class Install7
    {
        public Start_Menu_Shortcut7 start_menu_shortcut { get; set; }
        public Desktop_Shortcut7 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key7 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut7
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut7
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key7
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Zhcn
    {
        public Config12 config { get; set; }
    }

    public class Config12
    {
        public Install8[] install { get; set; }
    }

    public class Install8
    {
        public Start_Menu_Shortcut8 start_menu_shortcut { get; set; }
        public Desktop_Shortcut8 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key8 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut8
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut8
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key8
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }

    public class Zhtw
    {
        public Config13 config { get; set; }
    }

    public class Config13
    {
        public Install9[] install { get; set; }
    }

    public class Install9
    {
        public Start_Menu_Shortcut9 start_menu_shortcut { get; set; }
        public Desktop_Shortcut9 desktop_shortcut { get; set; }
        public Add_Remove_Programs_Key9 add_remove_programs_key { get; set; }
    }

    public class Start_Menu_Shortcut9
    {
        public string args { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Desktop_Shortcut9
    {
        public string args { get; set; }
        public string description { get; set; }
        public string link { get; set; }
        public string target { get; set; }
        public string working_dir { get; set; }
    }

    public class Add_Remove_Programs_Key9
    {
        public string display_name { get; set; }
        public string icon_path { get; set; }
        public string install_path { get; set; }
        public string locale { get; set; }
        public string root { get; set; }
        public string uid { get; set; }
        public string uninstall_path { get; set; }
    }
}
