using AndroidX.Compose.UI.Graphics.Vector;

namespace AndroidX.Compose;

/// <summary>
/// Material Design icons — C# mirror of Kotlin's
/// <c>androidx.compose.material.icons.Icons</c> companion object.
///
/// Each style class exposes the ~50 most commonly used Material icons
/// from the <c>material-icons-core</c> artifact as static <see cref="ImageVector"/>
/// properties — e.g. <c>Icons.Filled.Search</c>, <c>Icons.Outlined.Menu</c>,
/// <c>Icons.AutoMirrored.Filled.ArrowBack</c>.
///
/// <see cref="Default"/> is an alias for <see cref="Filled"/>, matching
/// Kotlin's <c>Icons.Default = Filled</c>. Pass the result to the
/// <see cref="Icon(ImageVector, string?)"/> constructor.
///
/// Sourced from <c>Xamarin.AndroidX.Compose.Material.Icons.Core</c>;
/// for the larger catalog see Compose's <c>material-icons-extended</c>
/// artifact (not yet bound).
/// </summary>
public static class Icons
{
    /// <summary>Material <c>Filled</c> icon style.</summary>
    public static class Filled
    {
        /// <summary>Material <c>AccountBox</c> icon.</summary>
        public static ImageVector AccountBox => AndroidX.Compose.Material.Icons.Filled.AccountBoxKt.GetAccountBox(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>AccountCircle</c> icon.</summary>
        public static ImageVector AccountCircle => AndroidX.Compose.Material.Icons.Filled.AccountCircleKt.GetAccountCircle(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Add</c> icon.</summary>
        public static ImageVector Add => AndroidX.Compose.Material.Icons.Filled.AddKt.GetAdd(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>AddCircle</c> icon.</summary>
        public static ImageVector AddCircle => AndroidX.Compose.Material.Icons.Filled.AddCircleKt.GetAddCircle(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>ArrowDropDown</c> icon.</summary>
        public static ImageVector ArrowDropDown => AndroidX.Compose.Material.Icons.Filled.ArrowDropDownKt.GetArrowDropDown(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Build</c> icon.</summary>
        public static ImageVector Build => AndroidX.Compose.Material.Icons.Filled.BuildKt.GetBuild(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Call</c> icon.</summary>
        public static ImageVector Call => AndroidX.Compose.Material.Icons.Filled.CallKt.GetCall(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Check</c> icon.</summary>
        public static ImageVector Check => AndroidX.Compose.Material.Icons.Filled.CheckKt.GetCheck(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>CheckCircle</c> icon.</summary>
        public static ImageVector CheckCircle => AndroidX.Compose.Material.Icons.Filled.CheckCircleKt.GetCheckCircle(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Clear</c> icon.</summary>
        public static ImageVector Clear => AndroidX.Compose.Material.Icons.Filled.ClearKt.GetClear(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Close</c> icon.</summary>
        public static ImageVector Close => AndroidX.Compose.Material.Icons.Filled.CloseKt.GetClose(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Create</c> icon.</summary>
        public static ImageVector Create => AndroidX.Compose.Material.Icons.Filled.CreateKt.GetCreate(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>DateRange</c> icon.</summary>
        public static ImageVector DateRange => AndroidX.Compose.Material.Icons.Filled.DateRangeKt.GetDateRange(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Delete</c> icon.</summary>
        public static ImageVector Delete => AndroidX.Compose.Material.Icons.Filled.DeleteKt.GetDelete(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Done</c> icon.</summary>
        public static ImageVector Done => AndroidX.Compose.Material.Icons.Filled.DoneKt.GetDone(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Edit</c> icon.</summary>
        public static ImageVector Edit => AndroidX.Compose.Material.Icons.Filled.EditKt.GetEdit(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Email</c> icon.</summary>
        public static ImageVector Email => AndroidX.Compose.Material.Icons.Filled.EmailKt.GetEmail(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Face</c> icon.</summary>
        public static ImageVector Face => AndroidX.Compose.Material.Icons.Filled.FaceKt.GetFace(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Favorite</c> icon.</summary>
        public static ImageVector Favorite => AndroidX.Compose.Material.Icons.Filled.FavoriteKt.GetFavorite(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>FavoriteBorder</c> icon.</summary>
        public static ImageVector FavoriteBorder => AndroidX.Compose.Material.Icons.Filled.FavoriteBorderKt.GetFavoriteBorder(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Home</c> icon.</summary>
        public static ImageVector Home => AndroidX.Compose.Material.Icons.Filled.HomeKt.GetHome(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Info</c> icon.</summary>
        public static ImageVector Info => AndroidX.Compose.Material.Icons.Filled.InfoKt.GetInfo(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>KeyboardArrowDown</c> icon.</summary>
        public static ImageVector KeyboardArrowDown => AndroidX.Compose.Material.Icons.Filled.KeyboardArrowDownKt.GetKeyboardArrowDown(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>KeyboardArrowUp</c> icon.</summary>
        public static ImageVector KeyboardArrowUp => AndroidX.Compose.Material.Icons.Filled.KeyboardArrowUpKt.GetKeyboardArrowUp(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>LocationOn</c> icon.</summary>
        public static ImageVector LocationOn => AndroidX.Compose.Material.Icons.Filled.LocationOnKt.GetLocationOn(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Lock</c> icon.</summary>
        public static ImageVector Lock => AndroidX.Compose.Material.Icons.Filled.LockKt.GetLock(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>MailOutline</c> icon.</summary>
        public static ImageVector MailOutline => AndroidX.Compose.Material.Icons.Filled.MailOutlineKt.GetMailOutline(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Menu</c> icon.</summary>
        public static ImageVector Menu => AndroidX.Compose.Material.Icons.Filled.MenuKt.GetMenu(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>MoreVert</c> icon.</summary>
        public static ImageVector MoreVert => AndroidX.Compose.Material.Icons.Filled.MoreVertKt.GetMoreVert(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Notifications</c> icon.</summary>
        public static ImageVector Notifications => AndroidX.Compose.Material.Icons.Filled.NotificationsKt.GetNotifications(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Person</c> icon.</summary>
        public static ImageVector Person => AndroidX.Compose.Material.Icons.Filled.PersonKt.GetPerson(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Phone</c> icon.</summary>
        public static ImageVector Phone => AndroidX.Compose.Material.Icons.Filled.PhoneKt.GetPhone(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Place</c> icon.</summary>
        public static ImageVector Place => AndroidX.Compose.Material.Icons.Filled.PlaceKt.GetPlace(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>PlayArrow</c> icon.</summary>
        public static ImageVector PlayArrow => AndroidX.Compose.Material.Icons.Filled.PlayArrowKt.GetPlayArrow(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Refresh</c> icon.</summary>
        public static ImageVector Refresh => AndroidX.Compose.Material.Icons.Filled.RefreshKt.GetRefresh(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Search</c> icon.</summary>
        public static ImageVector Search => AndroidX.Compose.Material.Icons.Filled.SearchKt.GetSearch(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Settings</c> icon.</summary>
        public static ImageVector Settings => AndroidX.Compose.Material.Icons.Filled.SettingsKt.GetSettings(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Share</c> icon.</summary>
        public static ImageVector Share => AndroidX.Compose.Material.Icons.Filled.ShareKt.GetShare(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>ShoppingCart</c> icon.</summary>
        public static ImageVector ShoppingCart => AndroidX.Compose.Material.Icons.Filled.ShoppingCartKt.GetShoppingCart(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Star</c> icon.</summary>
        public static ImageVector Star => AndroidX.Compose.Material.Icons.Filled.StarKt.GetStar(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>ThumbUp</c> icon.</summary>
        public static ImageVector ThumbUp => AndroidX.Compose.Material.Icons.Filled.ThumbUpKt.GetThumbUp(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
        /// <summary>Material <c>Warning</c> icon.</summary>
        public static ImageVector Warning => AndroidX.Compose.Material.Icons.Filled.WarningKt.GetWarning(AndroidX.Compose.Material.Icons.Icons.Filled.Instance);
    }

    /// <summary>Material <c>Outlined</c> icon style.</summary>
    public static class Outlined
    {
        /// <summary>Material <c>AccountBox</c> icon.</summary>
        public static ImageVector AccountBox => AndroidX.Compose.Material.Icons.Outlined.AccountBoxKt.GetAccountBox(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>AccountCircle</c> icon.</summary>
        public static ImageVector AccountCircle => AndroidX.Compose.Material.Icons.Outlined.AccountCircleKt.GetAccountCircle(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Add</c> icon.</summary>
        public static ImageVector Add => AndroidX.Compose.Material.Icons.Outlined.AddKt.GetAdd(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>AddCircle</c> icon.</summary>
        public static ImageVector AddCircle => AndroidX.Compose.Material.Icons.Outlined.AddCircleKt.GetAddCircle(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>ArrowDropDown</c> icon.</summary>
        public static ImageVector ArrowDropDown => AndroidX.Compose.Material.Icons.Outlined.ArrowDropDownKt.GetArrowDropDown(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Build</c> icon.</summary>
        public static ImageVector Build => AndroidX.Compose.Material.Icons.Outlined.BuildKt.GetBuild(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Call</c> icon.</summary>
        public static ImageVector Call => AndroidX.Compose.Material.Icons.Outlined.CallKt.GetCall(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Check</c> icon.</summary>
        public static ImageVector Check => AndroidX.Compose.Material.Icons.Outlined.CheckKt.GetCheck(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>CheckCircle</c> icon.</summary>
        public static ImageVector CheckCircle => AndroidX.Compose.Material.Icons.Outlined.CheckCircleKt.GetCheckCircle(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Clear</c> icon.</summary>
        public static ImageVector Clear => AndroidX.Compose.Material.Icons.Outlined.ClearKt.GetClear(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Close</c> icon.</summary>
        public static ImageVector Close => AndroidX.Compose.Material.Icons.Outlined.CloseKt.GetClose(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Create</c> icon.</summary>
        public static ImageVector Create => AndroidX.Compose.Material.Icons.Outlined.CreateKt.GetCreate(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>DateRange</c> icon.</summary>
        public static ImageVector DateRange => AndroidX.Compose.Material.Icons.Outlined.DateRangeKt.GetDateRange(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Delete</c> icon.</summary>
        public static ImageVector Delete => AndroidX.Compose.Material.Icons.Outlined.DeleteKt.GetDelete(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Done</c> icon.</summary>
        public static ImageVector Done => AndroidX.Compose.Material.Icons.Outlined.DoneKt.GetDone(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Edit</c> icon.</summary>
        public static ImageVector Edit => AndroidX.Compose.Material.Icons.Outlined.EditKt.GetEdit(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Email</c> icon.</summary>
        public static ImageVector Email => AndroidX.Compose.Material.Icons.Outlined.EmailKt.GetEmail(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Face</c> icon.</summary>
        public static ImageVector Face => AndroidX.Compose.Material.Icons.Outlined.FaceKt.GetFace(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Favorite</c> icon.</summary>
        public static ImageVector Favorite => AndroidX.Compose.Material.Icons.Outlined.FavoriteKt.GetFavorite(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>FavoriteBorder</c> icon.</summary>
        public static ImageVector FavoriteBorder => AndroidX.Compose.Material.Icons.Outlined.FavoriteBorderKt.GetFavoriteBorder(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Home</c> icon.</summary>
        public static ImageVector Home => AndroidX.Compose.Material.Icons.Outlined.HomeKt.GetHome(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Info</c> icon.</summary>
        public static ImageVector Info => AndroidX.Compose.Material.Icons.Outlined.InfoKt.GetInfo(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>KeyboardArrowDown</c> icon.</summary>
        public static ImageVector KeyboardArrowDown => AndroidX.Compose.Material.Icons.Outlined.KeyboardArrowDownKt.GetKeyboardArrowDown(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>KeyboardArrowUp</c> icon.</summary>
        public static ImageVector KeyboardArrowUp => AndroidX.Compose.Material.Icons.Outlined.KeyboardArrowUpKt.GetKeyboardArrowUp(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>LocationOn</c> icon.</summary>
        public static ImageVector LocationOn => AndroidX.Compose.Material.Icons.Outlined.LocationOnKt.GetLocationOn(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Lock</c> icon.</summary>
        public static ImageVector Lock => AndroidX.Compose.Material.Icons.Outlined.LockKt.GetLock(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>MailOutline</c> icon.</summary>
        public static ImageVector MailOutline => AndroidX.Compose.Material.Icons.Outlined.MailOutlineKt.GetMailOutline(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Menu</c> icon.</summary>
        public static ImageVector Menu => AndroidX.Compose.Material.Icons.Outlined.MenuKt.GetMenu(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>MoreVert</c> icon.</summary>
        public static ImageVector MoreVert => AndroidX.Compose.Material.Icons.Outlined.MoreVertKt.GetMoreVert(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Notifications</c> icon.</summary>
        public static ImageVector Notifications => AndroidX.Compose.Material.Icons.Outlined.NotificationsKt.GetNotifications(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Person</c> icon.</summary>
        public static ImageVector Person => AndroidX.Compose.Material.Icons.Outlined.PersonKt.GetPerson(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Phone</c> icon.</summary>
        public static ImageVector Phone => AndroidX.Compose.Material.Icons.Outlined.PhoneKt.GetPhone(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Place</c> icon.</summary>
        public static ImageVector Place => AndroidX.Compose.Material.Icons.Outlined.PlaceKt.GetPlace(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>PlayArrow</c> icon.</summary>
        public static ImageVector PlayArrow => AndroidX.Compose.Material.Icons.Outlined.PlayArrowKt.GetPlayArrow(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Refresh</c> icon.</summary>
        public static ImageVector Refresh => AndroidX.Compose.Material.Icons.Outlined.RefreshKt.GetRefresh(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Search</c> icon.</summary>
        public static ImageVector Search => AndroidX.Compose.Material.Icons.Outlined.SearchKt.GetSearch(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Settings</c> icon.</summary>
        public static ImageVector Settings => AndroidX.Compose.Material.Icons.Outlined.SettingsKt.GetSettings(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Share</c> icon.</summary>
        public static ImageVector Share => AndroidX.Compose.Material.Icons.Outlined.ShareKt.GetShare(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>ShoppingCart</c> icon.</summary>
        public static ImageVector ShoppingCart => AndroidX.Compose.Material.Icons.Outlined.ShoppingCartKt.GetShoppingCart(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Star</c> icon.</summary>
        public static ImageVector Star => AndroidX.Compose.Material.Icons.Outlined.StarKt.GetStar(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>ThumbUp</c> icon.</summary>
        public static ImageVector ThumbUp => AndroidX.Compose.Material.Icons.Outlined.ThumbUpKt.GetThumbUp(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
        /// <summary>Material <c>Warning</c> icon.</summary>
        public static ImageVector Warning => AndroidX.Compose.Material.Icons.Outlined.WarningKt.GetWarning(AndroidX.Compose.Material.Icons.Icons.Outlined.Instance);
    }

    /// <summary>Material <c>Rounded</c> icon style.</summary>
    public static class Rounded
    {
        /// <summary>Material <c>AccountBox</c> icon.</summary>
        public static ImageVector AccountBox => AndroidX.Compose.Material.Icons.Rounded.AccountBoxKt.GetAccountBox(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>AccountCircle</c> icon.</summary>
        public static ImageVector AccountCircle => AndroidX.Compose.Material.Icons.Rounded.AccountCircleKt.GetAccountCircle(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Add</c> icon.</summary>
        public static ImageVector Add => AndroidX.Compose.Material.Icons.Rounded.AddKt.GetAdd(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>AddCircle</c> icon.</summary>
        public static ImageVector AddCircle => AndroidX.Compose.Material.Icons.Rounded.AddCircleKt.GetAddCircle(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>ArrowDropDown</c> icon.</summary>
        public static ImageVector ArrowDropDown => AndroidX.Compose.Material.Icons.Rounded.ArrowDropDownKt.GetArrowDropDown(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Build</c> icon.</summary>
        public static ImageVector Build => AndroidX.Compose.Material.Icons.Rounded.BuildKt.GetBuild(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Call</c> icon.</summary>
        public static ImageVector Call => AndroidX.Compose.Material.Icons.Rounded.CallKt.GetCall(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Check</c> icon.</summary>
        public static ImageVector Check => AndroidX.Compose.Material.Icons.Rounded.CheckKt.GetCheck(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>CheckCircle</c> icon.</summary>
        public static ImageVector CheckCircle => AndroidX.Compose.Material.Icons.Rounded.CheckCircleKt.GetCheckCircle(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Clear</c> icon.</summary>
        public static ImageVector Clear => AndroidX.Compose.Material.Icons.Rounded.ClearKt.GetClear(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Close</c> icon.</summary>
        public static ImageVector Close => AndroidX.Compose.Material.Icons.Rounded.CloseKt.GetClose(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Create</c> icon.</summary>
        public static ImageVector Create => AndroidX.Compose.Material.Icons.Rounded.CreateKt.GetCreate(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>DateRange</c> icon.</summary>
        public static ImageVector DateRange => AndroidX.Compose.Material.Icons.Rounded.DateRangeKt.GetDateRange(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Delete</c> icon.</summary>
        public static ImageVector Delete => AndroidX.Compose.Material.Icons.Rounded.DeleteKt.GetDelete(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Done</c> icon.</summary>
        public static ImageVector Done => AndroidX.Compose.Material.Icons.Rounded.DoneKt.GetDone(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Edit</c> icon.</summary>
        public static ImageVector Edit => AndroidX.Compose.Material.Icons.Rounded.EditKt.GetEdit(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Email</c> icon.</summary>
        public static ImageVector Email => AndroidX.Compose.Material.Icons.Rounded.EmailKt.GetEmail(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Face</c> icon.</summary>
        public static ImageVector Face => AndroidX.Compose.Material.Icons.Rounded.FaceKt.GetFace(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Favorite</c> icon.</summary>
        public static ImageVector Favorite => AndroidX.Compose.Material.Icons.Rounded.FavoriteKt.GetFavorite(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>FavoriteBorder</c> icon.</summary>
        public static ImageVector FavoriteBorder => AndroidX.Compose.Material.Icons.Rounded.FavoriteBorderKt.GetFavoriteBorder(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Home</c> icon.</summary>
        public static ImageVector Home => AndroidX.Compose.Material.Icons.Rounded.HomeKt.GetHome(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Info</c> icon.</summary>
        public static ImageVector Info => AndroidX.Compose.Material.Icons.Rounded.InfoKt.GetInfo(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>KeyboardArrowDown</c> icon.</summary>
        public static ImageVector KeyboardArrowDown => AndroidX.Compose.Material.Icons.Rounded.KeyboardArrowDownKt.GetKeyboardArrowDown(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>KeyboardArrowUp</c> icon.</summary>
        public static ImageVector KeyboardArrowUp => AndroidX.Compose.Material.Icons.Rounded.KeyboardArrowUpKt.GetKeyboardArrowUp(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>LocationOn</c> icon.</summary>
        public static ImageVector LocationOn => AndroidX.Compose.Material.Icons.Rounded.LocationOnKt.GetLocationOn(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Lock</c> icon.</summary>
        public static ImageVector Lock => AndroidX.Compose.Material.Icons.Rounded.LockKt.GetLock(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>MailOutline</c> icon.</summary>
        public static ImageVector MailOutline => AndroidX.Compose.Material.Icons.Rounded.MailOutlineKt.GetMailOutline(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Menu</c> icon.</summary>
        public static ImageVector Menu => AndroidX.Compose.Material.Icons.Rounded.MenuKt.GetMenu(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>MoreVert</c> icon.</summary>
        public static ImageVector MoreVert => AndroidX.Compose.Material.Icons.Rounded.MoreVertKt.GetMoreVert(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Notifications</c> icon.</summary>
        public static ImageVector Notifications => AndroidX.Compose.Material.Icons.Rounded.NotificationsKt.GetNotifications(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Person</c> icon.</summary>
        public static ImageVector Person => AndroidX.Compose.Material.Icons.Rounded.PersonKt.GetPerson(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Phone</c> icon.</summary>
        public static ImageVector Phone => AndroidX.Compose.Material.Icons.Rounded.PhoneKt.GetPhone(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Place</c> icon.</summary>
        public static ImageVector Place => AndroidX.Compose.Material.Icons.Rounded.PlaceKt.GetPlace(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>PlayArrow</c> icon.</summary>
        public static ImageVector PlayArrow => AndroidX.Compose.Material.Icons.Rounded.PlayArrowKt.GetPlayArrow(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Refresh</c> icon.</summary>
        public static ImageVector Refresh => AndroidX.Compose.Material.Icons.Rounded.RefreshKt.GetRefresh(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Search</c> icon.</summary>
        public static ImageVector Search => AndroidX.Compose.Material.Icons.Rounded.SearchKt.GetSearch(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Settings</c> icon.</summary>
        public static ImageVector Settings => AndroidX.Compose.Material.Icons.Rounded.SettingsKt.GetSettings(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Share</c> icon.</summary>
        public static ImageVector Share => AndroidX.Compose.Material.Icons.Rounded.ShareKt.GetShare(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>ShoppingCart</c> icon.</summary>
        public static ImageVector ShoppingCart => AndroidX.Compose.Material.Icons.Rounded.ShoppingCartKt.GetShoppingCart(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Star</c> icon.</summary>
        public static ImageVector Star => AndroidX.Compose.Material.Icons.Rounded.StarKt.GetStar(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>ThumbUp</c> icon.</summary>
        public static ImageVector ThumbUp => AndroidX.Compose.Material.Icons.Rounded.ThumbUpKt.GetThumbUp(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
        /// <summary>Material <c>Warning</c> icon.</summary>
        public static ImageVector Warning => AndroidX.Compose.Material.Icons.Rounded.WarningKt.GetWarning(AndroidX.Compose.Material.Icons.Icons.Rounded.Instance);
    }

    /// <summary>Material <c>Sharp</c> icon style.</summary>
    public static class Sharp
    {
        /// <summary>Material <c>AccountBox</c> icon.</summary>
        public static ImageVector AccountBox => AndroidX.Compose.Material.Icons.Sharp.AccountBoxKt.GetAccountBox(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>AccountCircle</c> icon.</summary>
        public static ImageVector AccountCircle => AndroidX.Compose.Material.Icons.Sharp.AccountCircleKt.GetAccountCircle(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Add</c> icon.</summary>
        public static ImageVector Add => AndroidX.Compose.Material.Icons.Sharp.AddKt.GetAdd(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>AddCircle</c> icon.</summary>
        public static ImageVector AddCircle => AndroidX.Compose.Material.Icons.Sharp.AddCircleKt.GetAddCircle(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>ArrowDropDown</c> icon.</summary>
        public static ImageVector ArrowDropDown => AndroidX.Compose.Material.Icons.Sharp.ArrowDropDownKt.GetArrowDropDown(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Build</c> icon.</summary>
        public static ImageVector Build => AndroidX.Compose.Material.Icons.Sharp.BuildKt.GetBuild(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Call</c> icon.</summary>
        public static ImageVector Call => AndroidX.Compose.Material.Icons.Sharp.CallKt.GetCall(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Check</c> icon.</summary>
        public static ImageVector Check => AndroidX.Compose.Material.Icons.Sharp.CheckKt.GetCheck(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>CheckCircle</c> icon.</summary>
        public static ImageVector CheckCircle => AndroidX.Compose.Material.Icons.Sharp.CheckCircleKt.GetCheckCircle(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Clear</c> icon.</summary>
        public static ImageVector Clear => AndroidX.Compose.Material.Icons.Sharp.ClearKt.GetClear(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Close</c> icon.</summary>
        public static ImageVector Close => AndroidX.Compose.Material.Icons.Sharp.CloseKt.GetClose(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Create</c> icon.</summary>
        public static ImageVector Create => AndroidX.Compose.Material.Icons.Sharp.CreateKt.GetCreate(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>DateRange</c> icon.</summary>
        public static ImageVector DateRange => AndroidX.Compose.Material.Icons.Sharp.DateRangeKt.GetDateRange(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Delete</c> icon.</summary>
        public static ImageVector Delete => AndroidX.Compose.Material.Icons.Sharp.DeleteKt.GetDelete(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Done</c> icon.</summary>
        public static ImageVector Done => AndroidX.Compose.Material.Icons.Sharp.DoneKt.GetDone(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Edit</c> icon.</summary>
        public static ImageVector Edit => AndroidX.Compose.Material.Icons.Sharp.EditKt.GetEdit(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Email</c> icon.</summary>
        public static ImageVector Email => AndroidX.Compose.Material.Icons.Sharp.EmailKt.GetEmail(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Face</c> icon.</summary>
        public static ImageVector Face => AndroidX.Compose.Material.Icons.Sharp.FaceKt.GetFace(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Favorite</c> icon.</summary>
        public static ImageVector Favorite => AndroidX.Compose.Material.Icons.Sharp.FavoriteKt.GetFavorite(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>FavoriteBorder</c> icon.</summary>
        public static ImageVector FavoriteBorder => AndroidX.Compose.Material.Icons.Sharp.FavoriteBorderKt.GetFavoriteBorder(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Home</c> icon.</summary>
        public static ImageVector Home => AndroidX.Compose.Material.Icons.Sharp.HomeKt.GetHome(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Info</c> icon.</summary>
        public static ImageVector Info => AndroidX.Compose.Material.Icons.Sharp.InfoKt.GetInfo(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>KeyboardArrowDown</c> icon.</summary>
        public static ImageVector KeyboardArrowDown => AndroidX.Compose.Material.Icons.Sharp.KeyboardArrowDownKt.GetKeyboardArrowDown(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>KeyboardArrowUp</c> icon.</summary>
        public static ImageVector KeyboardArrowUp => AndroidX.Compose.Material.Icons.Sharp.KeyboardArrowUpKt.GetKeyboardArrowUp(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>LocationOn</c> icon.</summary>
        public static ImageVector LocationOn => AndroidX.Compose.Material.Icons.Sharp.LocationOnKt.GetLocationOn(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Lock</c> icon.</summary>
        public static ImageVector Lock => AndroidX.Compose.Material.Icons.Sharp.LockKt.GetLock(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>MailOutline</c> icon.</summary>
        public static ImageVector MailOutline => AndroidX.Compose.Material.Icons.Sharp.MailOutlineKt.GetMailOutline(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Menu</c> icon.</summary>
        public static ImageVector Menu => AndroidX.Compose.Material.Icons.Sharp.MenuKt.GetMenu(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>MoreVert</c> icon.</summary>
        public static ImageVector MoreVert => AndroidX.Compose.Material.Icons.Sharp.MoreVertKt.GetMoreVert(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Notifications</c> icon.</summary>
        public static ImageVector Notifications => AndroidX.Compose.Material.Icons.Sharp.NotificationsKt.GetNotifications(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Person</c> icon.</summary>
        public static ImageVector Person => AndroidX.Compose.Material.Icons.Sharp.PersonKt.GetPerson(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Phone</c> icon.</summary>
        public static ImageVector Phone => AndroidX.Compose.Material.Icons.Sharp.PhoneKt.GetPhone(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Place</c> icon.</summary>
        public static ImageVector Place => AndroidX.Compose.Material.Icons.Sharp.PlaceKt.GetPlace(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>PlayArrow</c> icon.</summary>
        public static ImageVector PlayArrow => AndroidX.Compose.Material.Icons.Sharp.PlayArrowKt.GetPlayArrow(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Refresh</c> icon.</summary>
        public static ImageVector Refresh => AndroidX.Compose.Material.Icons.Sharp.RefreshKt.GetRefresh(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Search</c> icon.</summary>
        public static ImageVector Search => AndroidX.Compose.Material.Icons.Sharp.SearchKt.GetSearch(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Settings</c> icon.</summary>
        public static ImageVector Settings => AndroidX.Compose.Material.Icons.Sharp.SettingsKt.GetSettings(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Share</c> icon.</summary>
        public static ImageVector Share => AndroidX.Compose.Material.Icons.Sharp.ShareKt.GetShare(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>ShoppingCart</c> icon.</summary>
        public static ImageVector ShoppingCart => AndroidX.Compose.Material.Icons.Sharp.ShoppingCartKt.GetShoppingCart(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Star</c> icon.</summary>
        public static ImageVector Star => AndroidX.Compose.Material.Icons.Sharp.StarKt.GetStar(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>ThumbUp</c> icon.</summary>
        public static ImageVector ThumbUp => AndroidX.Compose.Material.Icons.Sharp.ThumbUpKt.GetThumbUp(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
        /// <summary>Material <c>Warning</c> icon.</summary>
        public static ImageVector Warning => AndroidX.Compose.Material.Icons.Sharp.WarningKt.GetWarning(AndroidX.Compose.Material.Icons.Icons.Sharp.Instance);
    }

    /// <summary>Material <c>TwoTone</c> icon style.</summary>
    public static class TwoTone
    {
        /// <summary>Material <c>AccountBox</c> icon.</summary>
        public static ImageVector AccountBox => AndroidX.Compose.Material.Icons.TwoTone.AccountBoxKt.GetAccountBox(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>AccountCircle</c> icon.</summary>
        public static ImageVector AccountCircle => AndroidX.Compose.Material.Icons.TwoTone.AccountCircleKt.GetAccountCircle(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Add</c> icon.</summary>
        public static ImageVector Add => AndroidX.Compose.Material.Icons.TwoTone.AddKt.GetAdd(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>AddCircle</c> icon.</summary>
        public static ImageVector AddCircle => AndroidX.Compose.Material.Icons.TwoTone.AddCircleKt.GetAddCircle(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>ArrowDropDown</c> icon.</summary>
        public static ImageVector ArrowDropDown => AndroidX.Compose.Material.Icons.TwoTone.ArrowDropDownKt.GetArrowDropDown(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Build</c> icon.</summary>
        public static ImageVector Build => AndroidX.Compose.Material.Icons.TwoTone.BuildKt.GetBuild(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Call</c> icon.</summary>
        public static ImageVector Call => AndroidX.Compose.Material.Icons.TwoTone.CallKt.GetCall(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Check</c> icon.</summary>
        public static ImageVector Check => AndroidX.Compose.Material.Icons.TwoTone.CheckKt.GetCheck(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>CheckCircle</c> icon.</summary>
        public static ImageVector CheckCircle => AndroidX.Compose.Material.Icons.TwoTone.CheckCircleKt.GetCheckCircle(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Clear</c> icon.</summary>
        public static ImageVector Clear => AndroidX.Compose.Material.Icons.TwoTone.ClearKt.GetClear(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Close</c> icon.</summary>
        public static ImageVector Close => AndroidX.Compose.Material.Icons.TwoTone.CloseKt.GetClose(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Create</c> icon.</summary>
        public static ImageVector Create => AndroidX.Compose.Material.Icons.TwoTone.CreateKt.GetCreate(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>DateRange</c> icon.</summary>
        public static ImageVector DateRange => AndroidX.Compose.Material.Icons.TwoTone.DateRangeKt.GetDateRange(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Delete</c> icon.</summary>
        public static ImageVector Delete => AndroidX.Compose.Material.Icons.TwoTone.DeleteKt.GetDelete(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Done</c> icon.</summary>
        public static ImageVector Done => AndroidX.Compose.Material.Icons.TwoTone.DoneKt.GetDone(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Edit</c> icon.</summary>
        public static ImageVector Edit => AndroidX.Compose.Material.Icons.TwoTone.EditKt.GetEdit(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Email</c> icon.</summary>
        public static ImageVector Email => AndroidX.Compose.Material.Icons.TwoTone.EmailKt.GetEmail(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Face</c> icon.</summary>
        public static ImageVector Face => AndroidX.Compose.Material.Icons.TwoTone.FaceKt.GetFace(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Favorite</c> icon.</summary>
        public static ImageVector Favorite => AndroidX.Compose.Material.Icons.TwoTone.FavoriteKt.GetFavorite(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>FavoriteBorder</c> icon.</summary>
        public static ImageVector FavoriteBorder => AndroidX.Compose.Material.Icons.TwoTone.FavoriteBorderKt.GetFavoriteBorder(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Home</c> icon.</summary>
        public static ImageVector Home => AndroidX.Compose.Material.Icons.TwoTone.HomeKt.GetHome(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Info</c> icon.</summary>
        public static ImageVector Info => AndroidX.Compose.Material.Icons.TwoTone.InfoKt.GetInfo(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>KeyboardArrowDown</c> icon.</summary>
        public static ImageVector KeyboardArrowDown => AndroidX.Compose.Material.Icons.TwoTone.KeyboardArrowDownKt.GetKeyboardArrowDown(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>KeyboardArrowUp</c> icon.</summary>
        public static ImageVector KeyboardArrowUp => AndroidX.Compose.Material.Icons.TwoTone.KeyboardArrowUpKt.GetKeyboardArrowUp(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>LocationOn</c> icon.</summary>
        public static ImageVector LocationOn => AndroidX.Compose.Material.Icons.TwoTone.LocationOnKt.GetLocationOn(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Lock</c> icon.</summary>
        public static ImageVector Lock => AndroidX.Compose.Material.Icons.TwoTone.LockKt.GetLock(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>MailOutline</c> icon.</summary>
        public static ImageVector MailOutline => AndroidX.Compose.Material.Icons.TwoTone.MailOutlineKt.GetMailOutline(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Menu</c> icon.</summary>
        public static ImageVector Menu => AndroidX.Compose.Material.Icons.TwoTone.MenuKt.GetMenu(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>MoreVert</c> icon.</summary>
        public static ImageVector MoreVert => AndroidX.Compose.Material.Icons.TwoTone.MoreVertKt.GetMoreVert(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Notifications</c> icon.</summary>
        public static ImageVector Notifications => AndroidX.Compose.Material.Icons.TwoTone.NotificationsKt.GetNotifications(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Person</c> icon.</summary>
        public static ImageVector Person => AndroidX.Compose.Material.Icons.TwoTone.PersonKt.GetPerson(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Phone</c> icon.</summary>
        public static ImageVector Phone => AndroidX.Compose.Material.Icons.TwoTone.PhoneKt.GetPhone(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Place</c> icon.</summary>
        public static ImageVector Place => AndroidX.Compose.Material.Icons.TwoTone.PlaceKt.GetPlace(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>PlayArrow</c> icon.</summary>
        public static ImageVector PlayArrow => AndroidX.Compose.Material.Icons.TwoTone.PlayArrowKt.GetPlayArrow(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Refresh</c> icon.</summary>
        public static ImageVector Refresh => AndroidX.Compose.Material.Icons.TwoTone.RefreshKt.GetRefresh(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Search</c> icon.</summary>
        public static ImageVector Search => AndroidX.Compose.Material.Icons.TwoTone.SearchKt.GetSearch(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Settings</c> icon.</summary>
        public static ImageVector Settings => AndroidX.Compose.Material.Icons.TwoTone.SettingsKt.GetSettings(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Share</c> icon.</summary>
        public static ImageVector Share => AndroidX.Compose.Material.Icons.TwoTone.ShareKt.GetShare(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>ShoppingCart</c> icon.</summary>
        public static ImageVector ShoppingCart => AndroidX.Compose.Material.Icons.TwoTone.ShoppingCartKt.GetShoppingCart(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Star</c> icon.</summary>
        public static ImageVector Star => AndroidX.Compose.Material.Icons.TwoTone.StarKt.GetStar(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>ThumbUp</c> icon.</summary>
        public static ImageVector ThumbUp => AndroidX.Compose.Material.Icons.TwoTone.ThumbUpKt.GetThumbUp(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
        /// <summary>Material <c>Warning</c> icon.</summary>
        public static ImageVector Warning => AndroidX.Compose.Material.Icons.TwoTone.WarningKt.GetWarning(AndroidX.Compose.Material.Icons.Icons.TwoTone.Instance);
    }

    /// <summary>Alias for <see cref="Filled"/> — matches Kotlin's <c>Icons.Default</c>.</summary>
    public static class Default
    {
        /// <inheritdoc cref="Filled.AccountBox"/>
        public static ImageVector AccountBox => Filled.AccountBox;
        /// <inheritdoc cref="Filled.AccountCircle"/>
        public static ImageVector AccountCircle => Filled.AccountCircle;
        /// <inheritdoc cref="Filled.Add"/>
        public static ImageVector Add => Filled.Add;
        /// <inheritdoc cref="Filled.AddCircle"/>
        public static ImageVector AddCircle => Filled.AddCircle;
        /// <inheritdoc cref="Filled.ArrowDropDown"/>
        public static ImageVector ArrowDropDown => Filled.ArrowDropDown;
        /// <inheritdoc cref="Filled.Build"/>
        public static ImageVector Build => Filled.Build;
        /// <inheritdoc cref="Filled.Call"/>
        public static ImageVector Call => Filled.Call;
        /// <inheritdoc cref="Filled.Check"/>
        public static ImageVector Check => Filled.Check;
        /// <inheritdoc cref="Filled.CheckCircle"/>
        public static ImageVector CheckCircle => Filled.CheckCircle;
        /// <inheritdoc cref="Filled.Clear"/>
        public static ImageVector Clear => Filled.Clear;
        /// <inheritdoc cref="Filled.Close"/>
        public static ImageVector Close => Filled.Close;
        /// <inheritdoc cref="Filled.Create"/>
        public static ImageVector Create => Filled.Create;
        /// <inheritdoc cref="Filled.DateRange"/>
        public static ImageVector DateRange => Filled.DateRange;
        /// <inheritdoc cref="Filled.Delete"/>
        public static ImageVector Delete => Filled.Delete;
        /// <inheritdoc cref="Filled.Done"/>
        public static ImageVector Done => Filled.Done;
        /// <inheritdoc cref="Filled.Edit"/>
        public static ImageVector Edit => Filled.Edit;
        /// <inheritdoc cref="Filled.Email"/>
        public static ImageVector Email => Filled.Email;
        /// <inheritdoc cref="Filled.Face"/>
        public static ImageVector Face => Filled.Face;
        /// <inheritdoc cref="Filled.Favorite"/>
        public static ImageVector Favorite => Filled.Favorite;
        /// <inheritdoc cref="Filled.FavoriteBorder"/>
        public static ImageVector FavoriteBorder => Filled.FavoriteBorder;
        /// <inheritdoc cref="Filled.Home"/>
        public static ImageVector Home => Filled.Home;
        /// <inheritdoc cref="Filled.Info"/>
        public static ImageVector Info => Filled.Info;
        /// <inheritdoc cref="Filled.KeyboardArrowDown"/>
        public static ImageVector KeyboardArrowDown => Filled.KeyboardArrowDown;
        /// <inheritdoc cref="Filled.KeyboardArrowUp"/>
        public static ImageVector KeyboardArrowUp => Filled.KeyboardArrowUp;
        /// <inheritdoc cref="Filled.LocationOn"/>
        public static ImageVector LocationOn => Filled.LocationOn;
        /// <inheritdoc cref="Filled.Lock"/>
        public static ImageVector Lock => Filled.Lock;
        /// <inheritdoc cref="Filled.MailOutline"/>
        public static ImageVector MailOutline => Filled.MailOutline;
        /// <inheritdoc cref="Filled.Menu"/>
        public static ImageVector Menu => Filled.Menu;
        /// <inheritdoc cref="Filled.MoreVert"/>
        public static ImageVector MoreVert => Filled.MoreVert;
        /// <inheritdoc cref="Filled.Notifications"/>
        public static ImageVector Notifications => Filled.Notifications;
        /// <inheritdoc cref="Filled.Person"/>
        public static ImageVector Person => Filled.Person;
        /// <inheritdoc cref="Filled.Phone"/>
        public static ImageVector Phone => Filled.Phone;
        /// <inheritdoc cref="Filled.Place"/>
        public static ImageVector Place => Filled.Place;
        /// <inheritdoc cref="Filled.PlayArrow"/>
        public static ImageVector PlayArrow => Filled.PlayArrow;
        /// <inheritdoc cref="Filled.Refresh"/>
        public static ImageVector Refresh => Filled.Refresh;
        /// <inheritdoc cref="Filled.Search"/>
        public static ImageVector Search => Filled.Search;
        /// <inheritdoc cref="Filled.Settings"/>
        public static ImageVector Settings => Filled.Settings;
        /// <inheritdoc cref="Filled.Share"/>
        public static ImageVector Share => Filled.Share;
        /// <inheritdoc cref="Filled.ShoppingCart"/>
        public static ImageVector ShoppingCart => Filled.ShoppingCart;
        /// <inheritdoc cref="Filled.Star"/>
        public static ImageVector Star => Filled.Star;
        /// <inheritdoc cref="Filled.ThumbUp"/>
        public static ImageVector ThumbUp => Filled.ThumbUp;
        /// <inheritdoc cref="Filled.Warning"/>
        public static ImageVector Warning => Filled.Warning;
    }

    /// <summary>
    /// Auto-mirrored icons — variants that flip horizontally when the
    /// active <c>LayoutDirection</c> is RTL. Prefer these over the
    /// non-mirrored counterparts for directional affordances like back
    /// arrows and list bullets.
    /// </summary>
    public static class AutoMirrored
    {
        /// <summary>Auto-mirrored <c>Filled</c> icon style.</summary>
        public static class Filled
        {
            /// <summary>Auto-mirrored <c>ArrowBack</c> icon.</summary>
            public static ImageVector ArrowBack => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.ArrowBackKt.GetArrowBack(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>ArrowForward</c> icon.</summary>
            public static ImageVector ArrowForward => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.ArrowForwardKt.GetArrowForward(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>ExitToApp</c> icon.</summary>
            public static ImageVector ExitToApp => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.ExitToAppKt.GetExitToApp(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowLeft</c> icon.</summary>
            public static ImageVector KeyboardArrowLeft => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.KeyboardArrowLeftKt.GetKeyboardArrowLeft(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowRight</c> icon.</summary>
            public static ImageVector KeyboardArrowRight => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.KeyboardArrowRightKt.GetKeyboardArrowRight(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>List</c> icon.</summary>
            public static ImageVector List => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.ListKt.GetList(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
            /// <summary>Auto-mirrored <c>Send</c> icon.</summary>
            public static ImageVector Send => AndroidX.Compose.Material.Icons.AutoMirrored.Filled.SendKt.GetSend(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Filled.Instance);
        }

        /// <summary>Auto-mirrored <c>Outlined</c> icon style.</summary>
        public static class Outlined
        {
            /// <summary>Auto-mirrored <c>ArrowBack</c> icon.</summary>
            public static ImageVector ArrowBack => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.ArrowBackKt.GetArrowBack(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>ArrowForward</c> icon.</summary>
            public static ImageVector ArrowForward => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.ArrowForwardKt.GetArrowForward(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>ExitToApp</c> icon.</summary>
            public static ImageVector ExitToApp => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.ExitToAppKt.GetExitToApp(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowLeft</c> icon.</summary>
            public static ImageVector KeyboardArrowLeft => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.KeyboardArrowLeftKt.GetKeyboardArrowLeft(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowRight</c> icon.</summary>
            public static ImageVector KeyboardArrowRight => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.KeyboardArrowRightKt.GetKeyboardArrowRight(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>List</c> icon.</summary>
            public static ImageVector List => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.ListKt.GetList(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
            /// <summary>Auto-mirrored <c>Send</c> icon.</summary>
            public static ImageVector Send => AndroidX.Compose.Material.Icons.AutoMirrored.Outlined.SendKt.GetSend(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Outlined.Instance);
        }

        /// <summary>Auto-mirrored <c>Rounded</c> icon style.</summary>
        public static class Rounded
        {
            /// <summary>Auto-mirrored <c>ArrowBack</c> icon.</summary>
            public static ImageVector ArrowBack => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.ArrowBackKt.GetArrowBack(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>ArrowForward</c> icon.</summary>
            public static ImageVector ArrowForward => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.ArrowForwardKt.GetArrowForward(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>ExitToApp</c> icon.</summary>
            public static ImageVector ExitToApp => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.ExitToAppKt.GetExitToApp(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowLeft</c> icon.</summary>
            public static ImageVector KeyboardArrowLeft => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.KeyboardArrowLeftKt.GetKeyboardArrowLeft(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowRight</c> icon.</summary>
            public static ImageVector KeyboardArrowRight => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.KeyboardArrowRightKt.GetKeyboardArrowRight(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>List</c> icon.</summary>
            public static ImageVector List => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.ListKt.GetList(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
            /// <summary>Auto-mirrored <c>Send</c> icon.</summary>
            public static ImageVector Send => AndroidX.Compose.Material.Icons.AutoMirrored.Rounded.SendKt.GetSend(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Rounded.Instance);
        }

        /// <summary>Auto-mirrored <c>Sharp</c> icon style.</summary>
        public static class Sharp
        {
            /// <summary>Auto-mirrored <c>ArrowBack</c> icon.</summary>
            public static ImageVector ArrowBack => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.ArrowBackKt.GetArrowBack(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>ArrowForward</c> icon.</summary>
            public static ImageVector ArrowForward => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.ArrowForwardKt.GetArrowForward(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>ExitToApp</c> icon.</summary>
            public static ImageVector ExitToApp => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.ExitToAppKt.GetExitToApp(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowLeft</c> icon.</summary>
            public static ImageVector KeyboardArrowLeft => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.KeyboardArrowLeftKt.GetKeyboardArrowLeft(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowRight</c> icon.</summary>
            public static ImageVector KeyboardArrowRight => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.KeyboardArrowRightKt.GetKeyboardArrowRight(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>List</c> icon.</summary>
            public static ImageVector List => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.ListKt.GetList(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
            /// <summary>Auto-mirrored <c>Send</c> icon.</summary>
            public static ImageVector Send => AndroidX.Compose.Material.Icons.AutoMirrored.Sharp.SendKt.GetSend(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.Sharp.Instance);
        }

        /// <summary>Auto-mirrored <c>TwoTone</c> icon style.</summary>
        public static class TwoTone
        {
            /// <summary>Auto-mirrored <c>ArrowBack</c> icon.</summary>
            public static ImageVector ArrowBack => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.ArrowBackKt.GetArrowBack(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>ArrowForward</c> icon.</summary>
            public static ImageVector ArrowForward => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.ArrowForwardKt.GetArrowForward(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>ExitToApp</c> icon.</summary>
            public static ImageVector ExitToApp => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.ExitToAppKt.GetExitToApp(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowLeft</c> icon.</summary>
            public static ImageVector KeyboardArrowLeft => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.KeyboardArrowLeftKt.GetKeyboardArrowLeft(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>KeyboardArrowRight</c> icon.</summary>
            public static ImageVector KeyboardArrowRight => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.KeyboardArrowRightKt.GetKeyboardArrowRight(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>List</c> icon.</summary>
            public static ImageVector List => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.ListKt.GetList(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
            /// <summary>Auto-mirrored <c>Send</c> icon.</summary>
            public static ImageVector Send => AndroidX.Compose.Material.Icons.AutoMirrored.TwoTone.SendKt.GetSend(AndroidX.Compose.Material.Icons.Icons.AutoMirrored.TwoTone.Instance);
        }

        /// <summary>Alias for <see cref="Filled"/>.</summary>
        public static class Default
        {
            /// <inheritdoc cref="Filled.ArrowBack"/>
            public static ImageVector ArrowBack => Filled.ArrowBack;
            /// <inheritdoc cref="Filled.ArrowForward"/>
            public static ImageVector ArrowForward => Filled.ArrowForward;
            /// <inheritdoc cref="Filled.ExitToApp"/>
            public static ImageVector ExitToApp => Filled.ExitToApp;
            /// <inheritdoc cref="Filled.KeyboardArrowLeft"/>
            public static ImageVector KeyboardArrowLeft => Filled.KeyboardArrowLeft;
            /// <inheritdoc cref="Filled.KeyboardArrowRight"/>
            public static ImageVector KeyboardArrowRight => Filled.KeyboardArrowRight;
            /// <inheritdoc cref="Filled.List"/>
            public static ImageVector List => Filled.List;
            /// <inheritdoc cref="Filled.Send"/>
            public static ImageVector Send => Filled.Send;
        }
    }
}
