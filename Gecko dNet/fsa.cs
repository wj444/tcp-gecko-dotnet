﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using System.Windows.Forms;
using TCPTCPGecko;

using IconHelper;

namespace GeckoApp
{
    public class subFile:IComparable<subFile>
    {
        private String PName;
        private int PTag;
        private fileStructure PParent;
        public String name { 
            get { return PName; }
            set { PName = value; }
        }
        public int tag { get { return PTag; } }
        public fileStructure parent { get { return PParent; } }

        public subFile(String name, int tag,fileStructure parent)
        {
            PName = name;
            PTag = tag;
            PParent = parent;
        }

        public int CompareTo(subFile other)
        {
            return String.Compare(this.name, other.name);
        }
    }

    public class fileStructure:IComparable<fileStructure>
    {        
        private String PName;
        private int PTag;
        private fileStructure PParent;
        List<fileStructure> subFolders;
        List<subFile> subFiles;
        public String name { 
            get { return PName; }
            set { PName = value; }
        }
        public String Path { get { return (parent == null ? "" : parent.Path) + "/" + name; } }
        public int tag { get { return PTag; } }
        public fileStructure parent { get { return PParent; } }

        private fileStructure(String name,int tag,fileStructure parent)
        {
            PName = name;
            PTag = tag;
            PParent = parent;
            subFiles = new List<subFile>();
            subFolders = new List<fileStructure>();
        }

        public fileStructure(String name, int tag) : this(name,tag,null)
        { }

        public fileStructure addSubFolder(String name, int tag)
        {
            fileStructure nFS = new fileStructure(name, tag, this);
            subFolders.Add(nFS);
            return nFS;
        }

        public void addFile(String name, int tag)
        {
            subFile nSF = new subFile(name, tag, this);
            subFiles.Add(nSF);
        }

        public int CompareTo(fileStructure other)
        {
            return String.Compare(this.name, other.name);
        }

        public void Sort()
        {
            subFolders.Sort();
            subFiles.Sort();
            foreach (fileStructure nFS in subFolders)
                nFS.Sort();
        }

        public void ToTreeView(TreeView tv)
        {
            tv.Nodes.Clear();
            TreeNode root = tv.Nodes.Add(this.name);
            TreeNode subnode;
            foreach (fileStructure nFS in subFolders)
            {
                subnode = root.Nodes.Add(nFS.name);
                subnode.ImageIndex = 0;
                subnode.SelectedImageIndex = 1;
                subnode.Tag = nFS.tag;
                nFS.ToTreeNode(subnode);
            }
            foreach (subFile nSF in subFiles)
            {
                subnode = root.Nodes.Add(nSF.name);
                subnode.ImageIndex = 2;
                subnode.SelectedImageIndex = 2;
                subnode.Tag = nSF.tag;
            }
            if (subFiles.Count == 0 && subFolders.Count == 0)
            {
                subnode = root.Nodes.Add("");
                subnode.ImageIndex = 2;
                subnode.SelectedImageIndex = 2;
                subnode.Tag = -1;
            }
        }

        private void ToTreeNode(TreeNode tn)
        {
            TreeNode subnode;
            foreach (fileStructure nFS in subFolders)
            {
                subnode = tn.Nodes.Add(nFS.name);
                subnode.Tag = nFS.tag;
                subnode.ImageIndex = 0;
                subnode.SelectedImageIndex = 1;
                nFS.ToTreeNode(subnode);
            }
            foreach (subFile nSF in subFiles)
            {
                subnode = tn.Nodes.Add(nSF.name);
                subnode.ImageIndex = 2;
                subnode.SelectedImageIndex = 2;
                subnode.Tag = nSF.tag;
            }
            if (subFiles.Count == 0 && subFolders.Count == 0)
            {
                subnode = tn.Nodes.Add("");
                subnode.ImageIndex = 2;
                subnode.SelectedImageIndex = 2;
                subnode.Tag = -1;
            }
        }
    }

    public class fsaEntry
    {
        public UInt32 dataAddress;
        public UInt32 nameOffset;
        public UInt32 offset;
        public UInt32 entries;

        public UInt32 nameAddress;

        public fsaEntry(UInt32 UDataAddress, UInt32 UNameOffset, UInt32 UOffset,
            UInt32 UEntries, UInt32 UNameAddress)
        {
            dataAddress = UDataAddress;
            nameOffset = UNameOffset;
            offset = UOffset;
            entries = UEntries;
            nameAddress = UNameAddress;
        }
    }

    public class FSA
    {
        private TCPGecko gecko;
        private TreeView treeView;
        private fileStructure root;
        private TextBox fileSwapCode;

        private ImageList imgList;

        private List<fsaEntry> fsaTextPositions;
        private ExceptionHandler exceptionHandling;

        private int selectedFile;
        private String selFile;

        public FSA(TCPGecko UGecko, TreeView UTreeView, TextBox UFileSwapCode, ExceptionHandler UExceptionHandling)
        {
            exceptionHandling = UExceptionHandling;
            imgList = new ImageList();
#if !MONO
            System.Drawing.Icon ni = IconReader.GetFolderIcon(IconReader.IconSize.Small,
                IconReader.FolderType.Closed);
            imgList.Images.Add(ni);
            ni = IconReader.GetFolderIcon(IconReader.IconSize.Small,
                IconReader.FolderType.Open);
            imgList.Images.Add(ni);
            ni = IconReader.GetFileIcon("?.?", IconReader.IconSize.Small, false);
            imgList.Images.Add(ni);
#endif
            treeView = UTreeView;
            treeView.ImageList = imgList;
            treeView.NodeMouseClick += TreeView_NodeMouseClick;
            gecko = UGecko;
            fsaTextPositions = new List<fsaEntry>();

            fileSwapCode = UFileSwapCode;

            selectedFile = -1;
        }

        private String ReadString(Stream inputStream)
        {
            Byte[] buffer = new Byte[1];
            String result="";
            do
            {
                inputStream.Read(buffer, 0, 1);
                if (buffer[0] != 0)
                    result += (Char)buffer[0];
            } while (buffer[0] != 0);
            //result += " ";

            do
            {
                inputStream.Read(buffer, 0, 1);
                if (buffer[0] == 0)
                    result += " ";
            } while (buffer[0] == 0);
            
            return result;
        }

        public void DumpTree()
        {
            DumpTree("content");
        }
        public void DumpTree(params String[] folders)
        {
            UInt32 FSInit = 0x01060d70;
            UInt32 FSAddClient = 0x01061290;
            UInt32 FSDelClient = 0x0106129c;
            UInt32 FSInitCmdBlock = 0x01061498;
            UInt32 FSOpenDir = 0x01066f3c;
            UInt32 FSCloseDir = 0x01066fac;
            UInt32 FSReadDir = 0x0106702c;
            UInt32 memalign = gecko.peek(0x10049edc);
            UInt32 free = gecko.peek(0x100adc2c);

            try
            {
                UInt32 ret;
                ret = gecko.rpc(FSInit);

                UInt32 pClient = gecko.rpc(memalign, 0x1700, 0x20);
                if (pClient == 0) goto noClient;
                UInt32 pCmd = gecko.rpc(memalign, 0xA80, 0x20);
                if (pCmd == 0) goto noCmd;

                ret = gecko.rpc(FSAddClient, pClient, 0);
                ret = gecko.rpc(FSInitCmdBlock, pCmd);

                UInt32 pDh = gecko.rpc(memalign, 4, 4);
                if (pDh == 0) goto noDh;
                UInt32 pPath = gecko.rpc(memalign, 0x200, 0x20);
                if (pPath == 0) goto noPath;
                UInt32 pBuf = gecko.rpc(memalign, 0x200, 0x20);
                if (pBuf == 0) goto noBuf;

                root = new fileStructure("vol", -1);
                Queue<fileStructure> scanQueue = new Queue<fileStructure>();
                foreach (String item in folders)
                {
                    scanQueue.Enqueue(root.addSubFolder(item, -1));
                }
                while (scanQueue.Count > 0)
                {
                    fileStructure current = scanQueue.Dequeue();
                    using (MemoryStream ms = new MemoryStream(Encoding.ASCII.GetBytes(current.Path + "\0")))
                    {
                        gecko.Upload(pPath, pPath + (uint)ms.Length, ms);
                    }


                    ret = gecko.rpc(FSOpenDir, pClient, pCmd, pPath, pDh, 0xffffffff);
                    if (ret != 0) goto noDir;

                    UInt32 dh = gecko.peek(pDh);

                    do
                    {
                        ret = gecko.rpc(FSReadDir, pClient, pCmd, dh, pBuf, 0xffffffff);
                        if (ret != 0) break;

                        using (MemoryStream ms = new MemoryStream())
                        {
                            gecko.Dump(pBuf, pBuf + 0x200, ms);

                            Byte[] data = ms.ToArray();
                            UInt32 attr = ByteSwap.Swap(BitConverter.ToUInt32(data, 0));

                            String name = new String(Encoding.ASCII.GetChars(data, 0x64, 0x100));
                            name = name.Remove(name.IndexOf('\0'));

                            if ((attr & 0x80000000) != 0)
                            {
                                scanQueue.Enqueue(current.addSubFolder(name, -1));
                            }
                            else
                            {
                                current.addFile(name, -1);
                            }
                        }
                    } while (true);

                    gecko.rpc(FSCloseDir, pClient, pCmd, dh, 0);
                noDir:
                    continue;
                }

                gecko.rpc(free, pBuf);
            noBuf:
                gecko.rpc(free, pPath);
            noPath:
                gecko.rpc(free, pDh);
            noDh:

                ret = gecko.rpc(FSDelClient, pClient);

                gecko.rpc(free, pCmd);
            noCmd:
                gecko.rpc(free, pClient);
            noClient:

                if (root != null)
                {
                    root.Sort();
                    root.ToTreeView(treeView);
                }
            }
            catch (ETCPGeckoException e)
            {
                exceptionHandling.HandleException(e);
            }
            catch
            {
            }
        }

        private void TreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            int tag = -1;
            if (e.Node != null && e.Node.Tag != null && int.TryParse(e.Node.Tag.ToString(), out tag)
                && tag != -1)
            {
                UInt32 code = fsaTextPositions[tag].dataAddress - 0x79FFFFFC;
                fileSwapCode.Text =
                    GlobalFunctions.toHex(code) + " 00000008\r\n" +
                    GlobalFunctions.toHex(fsaTextPositions[tag].offset) + " " +
                    GlobalFunctions.toHex(fsaTextPositions[tag].entries);
                selFile = e.Node.Text;
            }
            else
            {
                fileSwapCode.Text = "";
            }
            selectedFile = tag;
        }
    }
}
