using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;

namespace NetworkUtil
{
    /// <summary>
    /// Provides access to information on a Network Location.
    /// </summary>
    [Serializable]
    [ComVisible(true)]
    public sealed class NetworkLocationInfo : ISerializable
    {
        /// <summary>
        /// Gets a value that indicates whether a network location is ready.
        /// </summary>
        public bool IsReady { get { return RootDirectory.Exists; } }
        /// <summary>
        /// Gets a value that indicates whether a network location is mapped.
        /// </summary>
        public bool IsMapped { get { return ShortcutFile.Exists; } }
        /// <summary>
        /// Gets the name of the share, such as \\192.168.100.1\data.
        /// </summary>
        public string Name { get { return RootDirectory.FullName; } }
        /// <summary>
        /// Gets the share name on the server
        /// </summary>
        public string ShareName { get; }
        /// <summary>
        /// Gets the name or IP address of the server
        /// </summary>
        public string ServerName { get; }
        /// <summary>
        /// Gets the share label of a network location
        /// </summary>
        public string ShareLabel
        {
            get { return ShortcutFile.Name; }
            set
            {
                if (!ShortcutFile.Exists)
                    throw new FileNotFoundException("Cannot find the network location shortcut file");

                ShortcutFile.MoveTo(Path.Combine(ShortcutFile.Parent.FullName, value));
            }
        }
        /// <summary>
        /// Gets the root directory of a network location.
        /// </summary>
        public DirectoryInfo RootDirectory { get; }

        private Lazy<DirectoryInfo> _shortcutFile;
        private DirectoryInfo ShortcutFile => _shortcutFile.Value;

        /// <summary>
        /// Provides access to information on the specified network location.
        /// </summary>
        [SecuritySafeCritical]
        public NetworkLocationInfo(string networkLocationPath)
            : this(networkLocationPath, null) { }

        private NetworkLocationInfo(string networkLocationPath, DirectoryInfo shortcutFile)
        {
            if (string.IsNullOrWhiteSpace(networkLocationPath))
                throw new ArgumentNullException(nameof(networkLocationPath));

            if (!networkLocationPath.StartsWith("\\\\"))
                throw new ArgumentException("The UNC path should be of the form \\\\server\\share");

            string root = networkLocationPath.TrimStart('\\');
            int i = root.IndexOf("\\");
            if (i < 0 || i + 1 == root.Length)
                throw new ArgumentException("The UNC path should be of the form \\\\server\\share");

            ServerName = root.Substring(0, i);
            root = root.Substring(i + 1);
            i = root.IndexOf("\\");
            ShareName = i < 0 ? root : root.Substring(0, i);

            if (string.IsNullOrWhiteSpace(ShareName))
                throw new ArgumentException("The UNC path should be of the form \\\\server\\share");

            RootDirectory = new DirectoryInfo(string.Concat("\\\\", ServerName, "\\", ShareName));

            _shortcutFile = new Lazy<DirectoryInfo>(() => shortcutFile ?? FindShortCutFile());
        }

        private DirectoryInfo FindShortCutFile()
        {
            DirectoryInfo network = new DirectoryInfo(NetworkLocationsPath);
            foreach (DirectoryInfo dir in network.EnumerateDirectories())
            {
                string path = GetShortCutPath(dir.Name);

                if (string.Equals(RootDirectory.FullName, path, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }

            return new DirectoryInfo(Path.Combine(NetworkLocationsPath, string.Concat(ShareName, " (", ServerName, ")")));
        }

        #region ISerializable methods

        /// <summary>
        /// Creates a new instance through deserialization
        /// </summary>
        private NetworkLocationInfo(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            ShareName = info.GetString(nameof(ShareName));
            ServerName = info.GetString(nameof(ServerName));
            RootDirectory = new DirectoryInfo(info.GetString(nameof(RootDirectory)));
            _shortcutFile = new DirectoryInfo(info.GetString(nameof(ShortcutFile)));
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(ShareName), ShareName);
            info.AddValue(nameof(ServerName), ServerName);
            info.AddValue(nameof(RootDirectory), RootDirectory.FullName);
            info.AddValue(nameof(ShortcutFile), ShortcutFile.FullName);
        }

        #endregion

        #region Object methods

        /// <summary>
        /// Returns a network location name as a string.
        /// </summary>
        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (obj is NetworkLocationInfo)
                return string.Equals(((NetworkLocationInfo)obj).Name, Name, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        #endregion

        #region Static Methods

        private static string NetworkLocationsPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Network Shortcuts");

        #region Shell Support

        private static Shell32.Folder _networkLocationsFolder;
        private static Shell32.Folder NetworkLocationsFolder
        {
            get
            {
                if (_networkLocationsFolder == null)
                {
                    Shell32.Shell shell = new Shell32.Shell();
                    _networkLocationsFolder = shell.NameSpace(NetworkLocationsPath);
                }
                return _networkLocationsFolder;
            }
        }
        private static string GetShortCutPath(string name)
        {
            try
            {
                Shell32.FolderItem folderItem = NetworkLocationsFolder?.ParseName(name);

                if (folderItem == null || !folderItem.IsLink)
                    return null;

                Shell32.ShellLinkObject link = (Shell32.ShellLinkObject)folderItem.GetLink;
                return link.Path;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Gets all of the network locations found.
        /// </summary>
        public static NetworkLocationInfo[] GetNetworkLocations()
        {
            if (NetworkLocationsFolder == null)
                return new NetworkLocationInfo[] { };

            DirectoryInfo networkShortcuts = new DirectoryInfo(NetworkLocationsPath);

            DirectoryInfo[] subDirectories = networkShortcuts.GetDirectories();

            NetworkLocationInfo[] locations = new NetworkLocationInfo[subDirectories.Length];
            if (subDirectories.Length == 0)
                return locations;

            int i = 0;
            foreach (DirectoryInfo dir in subDirectories)
            {
                string networkLocationPath = GetShortCutPath(dir.Name);

                if (string.IsNullOrWhiteSpace(networkLocationPath))
                    continue;

                try
                {
                    NetworkLocationInfo info = new NetworkLocationInfo(networkLocationPath, dir);

                    locations[i++] = info;
                }
                catch { continue; }
            }

            if (i < locations.Length)
                Array.Resize(ref locations, i);

            return locations;
        }

        #endregion
    }
}
