using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Ibob;

// Apps like WhatsApp and Outlook don't drag real file paths; they drag the file's bytes ("virtual files").
// This reads each one and saves it into a folder so it becomes a normal file.
static class VirtualFiles
{
    const int DescriptorSize = 592;          // size of one FILEDESCRIPTORW entry
    const int NameOffset = 72;               // where the file name starts inside it
    const int AttributesOffset = 36;
    const uint DirectoryAttribute = 0x10;

    public static bool Present(System.Windows.IDataObject data) => data.GetDataPresent("FileGroupDescriptorW");

    public static List<string> Save(System.Windows.IDataObject data, string folder)
    {
        var saved = new List<string>();
        var descriptor = ((MemoryStream)data.GetData("FileGroupDescriptorW")).ToArray();
        int count = BitConverter.ToInt32(descriptor, 0);
        short contentsFormat = unchecked((short)DataFormats.GetDataFormat("FileContents").Id);

        for (int i = 0; i < count; i++)
        {
            int start = 4 + i * DescriptorSize;
            if ((BitConverter.ToUInt32(descriptor, start + AttributesOffset) & DirectoryAttribute) != 0) continue;

            string name = Encoding.Unicode.GetString(descriptor, start + NameOffset, 520).Split('\0')[0];
            var format = new FORMATETC
            {
                cfFormat = contentsFormat, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = i,
                tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
            };

            ((ComDataObject)data).GetData(ref format, out var medium);
            try
            {
                string path = Converters.UniquePath(folder, Path.GetFileName(name));
                File.WriteAllBytes(path, Read(medium));
                saved.Add(path);
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        return saved;
    }

    // The bytes arrive either as a COM stream or as a block of memory.
    static byte[] Read(STGMEDIUM medium)
    {
        if (medium.tymed == TYMED.TYMED_HGLOBAL)
        {
            int size = (int)GlobalSize(medium.unionmember);
            var bytes = new byte[size];
            Marshal.Copy(GlobalLock(medium.unionmember), bytes, 0, size);
            GlobalUnlock(medium.unionmember);
            return bytes;
        }

        var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
        using var output = new MemoryStream();
        var chunk = new byte[81920];
        IntPtr readCount = Marshal.AllocCoTaskMem(4);
        try
        {
            while (true)
            {
                stream.Read(chunk, chunk.Length, readCount);
                int read = Marshal.ReadInt32(readCount);
                if (read == 0) break;
                output.Write(chunk, 0, read);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(readCount);
            Marshal.ReleaseComObject(stream);
        }
        return output.ToArray();
    }

    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr handle);
}
