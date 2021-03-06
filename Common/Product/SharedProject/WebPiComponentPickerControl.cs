// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudioTools.Project
{
    [Guid("B7773A32-2EE5-4844-9630-F14768A5D03C")]
    internal partial class WebPiComponentPickerControl : UserControl
    {
        private readonly List<PackageInfo> _packages = new List<PackageInfo>();
        private const string _defaultFeeds = "https://www.microsoft.com/web/webpi/5.0/webproductlist.xml";
        private ListViewSorter _sorter = new ListViewSorter();

        public WebPiComponentPickerControl()
        {
            InitializeComponent();

            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._productsList.ItemSelectionChanged += new ListViewItemSelectionChangedEventHandler(this.ProductsListItemSelectionChanged);
            this._productsList.ListViewItemSorter = this._sorter;
            this._productsList.DoubleClick += new EventHandler(this.ProductsListDoubleClick);
            this._productsList.ColumnClick += new ColumnClickEventHandler(this.ProductsListColumnClick);
        }

        private void ProductsListColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == this._sorter.Column)
            {
                if (this._sorter.Order == SortOrder.Ascending)
                {
                    this._sorter.Order = SortOrder.Descending;
                }
                else
                {
                    this._sorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                this._sorter.Column = e.Column;
                this._sorter.Order = SortOrder.Ascending;
            }
            this._productsList.Sort();
        }

        private void ProductsListDoubleClick(object sender, EventArgs e)
        {
            NativeMethods.SendMessage(
                NativeMethods.GetParent(NativeMethods.GetParent(NativeMethods.GetParent(this.Handle))),
                (uint)VSConstants.CPDN_SELDBLCLICK,
                IntPtr.Zero,
                this.Handle
            );
        }

        private void ProductsListItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            NativeMethods.SendMessage(
                NativeMethods.GetParent(NativeMethods.GetParent(NativeMethods.GetParent(this.Handle))),
                (uint)VSConstants.CPDN_SELCHANGED,
                IntPtr.Zero,
                this.Handle
            );
        }

        private async Task RequestFeeds(string feedSource)
        {
            try
            {
                await Task.Run(() => GetFeeds(feedSource));
            }
            catch (Exception ex)
            {
                if (ex.IsCriticalException())
                {
                    throw;
                }

                MessageBox.Show(SR.GetString(SR.WebPiFeedError, feedSource, ex.Message));

                var fullMessage = SR.GetString(SR.WebPiFeedError, feedSource, ex);
                Trace.WriteLine(fullMessage);
                try
                {
                    ActivityLog.LogError("WebPiComponentPickerControl", fullMessage);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void GetFeeds(string feed)
        {
            var doc = new XPathDocument(feed);
            var mngr = new XmlNamespaceManager(new NameTable());
            mngr.AddNamespace("x", "http://www.w3.org/2005/Atom");

            var nav = doc.CreateNavigator();

            var nodes = nav.Select("/x:feed/x:entry", mngr);
            foreach (XPathNavigator node in nodes)
            {
                var title = node.Select("x:title", mngr);
                var updated = node.Select("x:updated", mngr);
                var productId = node.Select("x:productId", mngr);

                string titleVal = null;
                foreach (XPathNavigator titleNode in title)
                {
                    titleVal = titleNode.Value;
                    break;
                }

                string updatedVal = null;
                foreach (XPathNavigator updatedNode in updated)
                {
                    updatedVal = updatedNode.Value;
                }

                string productIdVal = null;
                foreach (XPathNavigator productIdNode in productId)
                {
                    productIdVal = productIdNode.Value;
                }

                if (titleVal != null && updatedVal != null && productIdVal != null)
                {
                    var newPackage = new PackageInfo(
                        titleVal,
                        updatedVal,
                        productIdVal,
                        feed
                    );

                    this._packages.Add(newPackage);

                    try
                    {
                        BeginInvoke(new Action<object>(this.AddPackage), newPackage);
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }
            }
        }

        private void AddPackage(object package)
        {
            var pkgInfo = (PackageInfo)package;
            var item = new ListViewItem(
                new[] {
                    pkgInfo.Title,
                    pkgInfo.Updated,
                    pkgInfo.ProductId,
                    pkgInfo.Feed
                }
            );

            item.Tag = pkgInfo;
            this._productsList.Items.Add(item);
        }

        private class PackageInfo
        {
            public readonly string Title;
            public readonly string Updated;
            public readonly string ProductId;
            public readonly string Feed;

            public PackageInfo(string title, string updated, string productId, string feed)
            {
                this.Title = title;
                this.Updated = updated;
                this.ProductId = productId;
                this.Feed = feed;
            }
        }

        private class ListViewSorter : IComparer
        {
            public SortOrder Order;
            public int Column;

            #region IComparer Members

            public int Compare(object x, object y)
            {
                var itemX = (ListViewItem)x;
                var itemY = (ListViewItem)y;

                int? res = null;
                if (this.Column == 1)
                {
                    DateTime dtX, dtY;
                    if (DateTime.TryParse(itemX.SubItems[1].Text, out dtX) &&
                        DateTime.TryParse(itemY.SubItems[1].Text, out dtY))
                    {
                        res = dtX.CompareTo(dtY);
                    }
                }

                if (res == null)
                {
                    res = StringComparer.CurrentCultureIgnoreCase.Compare(itemX.SubItems[0].Text, itemY.SubItems[0].Text);
                }

                if (this.Order == SortOrder.Descending)
                {
                    return -res.Value;
                }
                return res.Value;
            }

            #endregion
        }
        private void AddNewFeedClick(object sender, EventArgs e)
        {
            RequestFeeds(this._newFeedUrl.Text).DoNotWait();
        }

        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_INITDIALOG:
                    SetWindowStyleOnStaticHostControl();
                    goto default;
                case VSConstants.CPPM_INITIALIZELIST:
                    RequestFeeds(_defaultFeeds).DoNotWait();
                    break;
                case VSConstants.CPPM_SETMULTISELECT:
                    this._productsList.MultiSelect = (m.WParam != IntPtr.Zero);
                    break;
                case VSConstants.CPPM_CLEARSELECTION:
                    this._productsList.SelectedItems.Clear();
                    break;
                case VSConstants.CPPM_QUERYCANSELECT:
                    Marshal.WriteInt32(
                        m.LParam,
                        (this._productsList.SelectedItems.Count > 0) ? 1 : 0
                    );
                    break;
                case VSConstants.CPPM_GETSELECTION:
                    var items = new PackageInfo[this._productsList.SelectedItems.Count];
                    for (var i = 0; i < items.Length; i++)
                    {
                        items[i] = (PackageInfo)this._productsList.SelectedItems[0].Tag;
                    }
                    var count = items != null ? items.Length : 0;
                    Marshal.WriteByte(m.WParam, Convert.ToByte(count));
                    if (count > 0)
                    {
                        var ppItems = Marshal.AllocCoTaskMem(
                          count * Marshal.SizeOf(typeof(IntPtr)));
                        for (var i = 0; i < count; i++)
                        {
                            var pItem = Marshal.AllocCoTaskMem(
                                    Marshal.SizeOf(typeof(VSCOMPONENTSELECTORDATA)));
                            Marshal.WriteIntPtr(
                                ppItems + i * Marshal.SizeOf(typeof(IntPtr)),
                                pItem);
                            var data = new VSCOMPONENTSELECTORDATA()
                            {
                                dwSize = (uint)Marshal.SizeOf(typeof(VSCOMPONENTSELECTORDATA)),
                                bstrFile = items[i].Feed,
                                bstrTitle = items[i].ProductId,
                                bstrProjRef = items[i].Title,
                                type = VSCOMPONENTTYPE.VSCOMPONENTTYPE_Custom
                            };
                            Marshal.StructureToPtr(data, pItem, false);
                        }
                        Marshal.WriteIntPtr(m.LParam, ppItems);
                    }
                    break;
                case NativeMethods.WM_SIZE:
                    var parentHwnd = NativeMethods.GetParent(this.Handle);

                    if (parentHwnd != IntPtr.Zero)
                    {
                        var grandParentHwnd = NativeMethods.GetParent(parentHwnd);

                        User32RECT parentClientRect, grandParentClientRect;
                        if (grandParentHwnd != IntPtr.Zero &&
                            NativeMethods.GetClientRect(parentHwnd, out parentClientRect) &&
                                NativeMethods.GetClientRect(grandParentHwnd, out grandParentClientRect))
                        {
                            var width = grandParentClientRect.Width;
                            var height = grandParentClientRect.Height;

                            if ((parentClientRect.Width != width) || (parentClientRect.Height != height))
                            {
                                NativeMethods.MoveWindow(parentHwnd, 0, 0, width, height, true);
                                this.Width = width;
                                this.Height = height;
                            }
                            this.Refresh();
                        }
                    }
                    break;
                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        private void NewFeedUrlTextChanged(object sender, EventArgs e)
        {
            try
            {
                new Uri(this._newFeedUrl.Text);
                this._addNewFeed.Enabled = true;
            }
            catch (UriFormatException)
            {
                this._addNewFeed.Enabled = false;
            }
        }

        /// <summary>
        /// VS hosts us in a static control ("This is a static!") but that control doesn't
        /// have the WS_EX_CONTROLPARENT style like it should (and like our UserControl properly
        /// gets because it's a ContainerControl).  This causes an infinite loop when we start 
        /// trying to loop through the controls if the user navigates away from the control.
        /// 
        /// http://support.microsoft.com/kb/149501
        /// 
        /// So we go in and muck about with the This is a static! control's window style so that
        /// we don't hang VS if the user alt-tabs away.
        /// </summary>
        private void SetWindowStyleOnStaticHostControl()
        {
            var target = (NativeMethods.GetParent(this.Handle));
            NativeMethods.SetWindowLong(
                target,
                NativeMethods.GWL_EXSTYLE,
                NativeMethods.WS_EX_CONTROLPARENT | NativeMethods.GetWindowLong(target, NativeMethods.GWL_EXSTYLE)
            );
        }
    }
}


