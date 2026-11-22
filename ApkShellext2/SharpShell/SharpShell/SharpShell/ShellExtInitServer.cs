using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SharpShell.Extensions;
using SharpShell.Interop;

namespace SharpShell
{
    /// <summary>
    /// The ShellExtInitServer is the base class for Shell Extension Servers
    /// that implement IShellExtInit.
    /// </summary>
    public abstract class ShellExtInitServer : SharpShellServer, IShellExtInit
    {
        #region Implementation of IShellExtInit

        /// <summary>
        /// Initializes the shell extension with the parent folder and data object.
        /// </summary>
        /// <param name="pidlFolder">The pidl of the parent folder.</param>
        /// <param name="pDataObj">The data object pointer.</param>
        /// <param name="hKeyProgID">The handle to the key prog id.</param>
        void IShellExtInit.Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID)
        {
            //  First call into the extension, and the shell calls it before anything else.
            //  The interop signature is void (no PreserveSig), so an escaping exception is
            //  the only way to signal failure - and it would unwind through explorer's
            //  frames. Guard it, log it, and leave the selection empty: every downstream
            //  entry point is written to survive "nothing selected" and returns a proper
            //  HRESULT, so the worst case is a menu that does not appear.
            try
            {
                Log("Initializing shell extension...");

                //  If we have the folder PIDL, we can get the parent folder.
                if (pidlFolder != IntPtr.Zero)
                {
                    var stringBuilder = new StringBuilder(260);
                    if (User32.SHGetPathFromIDListW(pidlFolder, stringBuilder))
                    {
                        //  Set parent folder path.
                        FolderPath = stringBuilder.ToString();
                    }
                }

                //  If we have the data object, we can use it to get the selected item paths.
                if (pDataObj != IntPtr.Zero)
                {
                    //  Create the IDataObject from the provided pDataObj.
                    var dataObject = (System.Runtime.InteropServices.ComTypes.IDataObject) Marshal.GetObjectForIUnknown(pDataObj);

                    //  Add the set of files to the selected file paths.
                    selectedItemPaths = dataObject.GetFileList();
                }

                Log(string.Format("Shell extension initialised.{0}Parent folder: {1}{0}Items: {0}{2}",
                    Environment.NewLine, FolderPath ?? "<none>", string.Join(System.Environment.NewLine, selectedItemPaths)));
            }
            catch (Exception exception)
            {
                LogError("An exception ocurred initializing the shell extension.", exception);
            }
        }

        #endregion
        
        /// <summary>
        /// The selected item paths.
        /// </summary>
        private List<string> selectedItemPaths = new List<string>();

        /// <summary>
        /// Gets the selected item paths.
        /// </summary>
        public IEnumerable<string> SelectedItemPaths
        {
            get { return selectedItemPaths; }
        }

        /// <summary>
        /// Gets the folder path.
        /// </summary>
        /// <value>
        /// The folder path.
        /// </value>
        public string FolderPath { get; private set; }
    }
}
