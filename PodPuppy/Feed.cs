// PodPuppy - a simple podcast receiver for Windows
// Copyright (c) Felix Watts 2008 (felixwatts@gmail.com)
// https://github.com/felixwatts/PodPuppy
//
// This file is distributed under the Creative Commons Attribution-NonCommercial 4.0 International Licence
// https://creativecommons.org/licenses/by-nc/4.0/

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Xml;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using MarcClifton; // keyed list

namespace PodPuppy
{
    public enum FeedStatus
    {
        /// <summary>
        /// Awaiting a free DownloadWorker.
        /// </summary>
        Pending,
        /// <summary>
        /// Currently being downloaded.
        /// </summary>
        Downloading,
        /// <summary>
        /// All items in the feed have already been downloaded succesfully.
        /// </summary>
        Complete,
        /// <summary>
        /// Currently re-fetching the feed XML.
        /// </summary>
        Refreshing,
        /// <summary>
        /// Something went wrong when processing this feed. (Note: this is for feed errors. item download errors are marked with item status and on feed with CompleteWithErrors.
        /// </summary>
        Error,
        /// <summary>
        /// The feed has been unsubscribed by the user.
        /// </summary>
        Removed,
        /// <summary>
        /// All items are complete or errored.
        /// </summary>
        CompleteWithErrors,
        /// <summary>
        /// just about to start a new refresh because the last one resulted in a redirect to another url.
        /// </summary>
        Redirecting,
    }

    /// <summary>
    /// What to do with old items. Set on a per-feed basis.
    /// </summary>
    public enum ArchiveMode
    {
        /// <summary>
        /// Downloaded items are kept forever.
        /// </summary>
        Keep,
        /// <summary>
        /// Items downloaded more than 1 week ago will be deleted when the feed is refreshed.
        /// </summary>
        KeepLatest,
        /// <summary>
        /// Items downloaded more than 1 week ago will be deleted when the feed is refreshed.
        /// </summary>
        DeleteAfterOneWeek,
        /// <summary>
        /// Items downloaded more than 1 month ago will be deleted when the feed is refreshed.
        /// </summary>
        DeleteAfterOneMonth,
        /// <summary>
        /// Downloaded items that are not found in the latest feed will be deleted.
        /// </summary>
        MatchFeed,
    }

    public enum FeedRefreshResult
    {
        /// <summary>
        /// refresh completed succesfully.
        /// </summary>
        Success, 
        /// <summary>
        /// source did not contain a feed.
        /// </summary>
        InvalidData,
        /// <summary>
        /// unable to connect to source.
        /// </summary>
        UnableToConnect,
        /// <summary>
        /// source contained a redirect to a feed, url has been changed.
        /// </summary>
        Redirect,
        /// <summary>
        /// Canceled by user.
        /// </summary>
        Canceled,
        /// <summary>
        /// The xml at the url was OPML rather than a podcast. If this is returned the feed is invalid and cannot be used.
        /// (this happens when the user tries to subscribe to OPML directly rather than setting the opml source in the 
        /// options dialog. We handle this return status to ask the user whether they want to set the opml source to this url.
        /// </summary>
        IsOPML
    }

    /// <summary>
    /// Represents a single RSS1.0, RSS2.0, RDF or Atom news feed.
    /// </summary>
    public class Feed : ListViewItem, IXmlSerializable
    {
        #region Private Members

        // TODO remove in version 0.7.0.0
        private static int _nextPriority = 0;

        private const string Rss1Namespace = "http://purl.org/rss/1.0/";
        private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string AtomNamespace = "http://www.w3.org/2005/Atom";
        private const string XHTML1_0Namespace = "http://www.w3.org/1999/xhtml";

        private static Regex _altLinkRgx = null;
        private static Regex _altLinkURLRgx = null;

        /// <summary>
        /// If an error occurs on the first refresh we want to behave differently
        /// (show message box and remove ourself, rather than show error message in status
        /// string) so this is to mark out the first refresh.
        /// </summary>
        private bool _neverRefreshed;

        private FeedStatus _status;

        //private int _rank;
        /// <summary>
        /// Represents the feeds position in the priority. NOTE: this is only used
        /// in saving and restoring the app state and is not kept up to date during 
        /// app running.
        /// </summary>
        //public int Rank
        //{
        //    get { return _rank; }
        //    set { _rank = value; }
        //}

        /// <summary>
        /// The URL where the feed XML was downloaded. From XML
        /// </summary>
        protected string _url;

        /// <summary>
        /// Represents the Link node in some feed types. Not used. From XML
        /// </summary>
        protected string _link;

        /// <summary>
        /// The title of the feed. From XML
        /// </summary>
        protected string _title;

        /// <summary>
        /// The description of the feed. From XML
        /// </summary>
        protected string _description;

        private string _folder; // relative to podcasts folder

        protected string _errorMessage;

        private int _priority = -1;

        // keyed by item url.
        protected KeyedList<string, Item> _items;

        private ArchiveMode _archiveMode;

        private bool _syncronise;

        // a background worker used to re-fetch the feed xml.
        private BackgroundWorker _refreshWorker;

        private bool _addedDynamically;

        // Tagging

        private string _albumTag = "%p";
        private string _genreTag = "Podcast";
        private string _artistTag = "";
        private string _titleTag = "%t";
        private string _itemFilenamePattern = "%t";
        private bool _applyAlbumTag = true;
        private bool _applyGenreTag = true;
        private bool _applyArtistTag = false;
        private bool _applyTrackTitleTag = true;

        private bool _overwriteExisingTags = true;

        private FeedFetcher _fetcher = null;

        #endregion

        #region Construction

        static Feed()
        {
            _altLinkRgx = new Regex("<link(.+?)>", RegexOptions.Compiled);
            _altLinkURLRgx = new Regex("href=\"(.+?)\"");
        }

        public Feed()
            : base()
        {
            _items = new KeyedList<string, Item>();

            ListViewSubItem statusColumn = new ListViewSubItem();
            SubItems.Add(statusColumn);

            ListViewSubItem syncedColumn = new ListViewSubItem();
            SubItems.Add(syncedColumn);

            ListViewSubItem priorityColumn = new ListViewSubItem();
            SubItems.Add(priorityColumn);

            ListViewSubItem latestColumn = new ListViewSubItem();
            SubItems.Add(latestColumn);

            // this is the constructor used by the deserialiser. If this 
            // feed is being deserialsed its a safe bet it has been refeshed before.
            // Note: its not the end of the world if this is set wrong - only means
            // the error message will appear in the form rather than in a MessageBox.
            _neverRefreshed = false;

            _archiveMode = ArchiveMode.Keep;

            _fetcher = new FeedFetcher();
        }

        public Feed(string url)
            : this()
        {
            _url = url;
            _status = FeedStatus.Pending;
            _title = url; // temporary until we fetch the title from the server.
            Text = _title;
            _neverRefreshed = true;
            _syncronise = true;
        }

        #endregion

        #region Events and Delegates

        public event ItemDownloadFinishedHandler ItemDownloaded;

        public delegate void FeedFirstRefreshCompletedHandler(Feed subject, FeedRefreshResult refreshResult);

        public event FeedFirstRefreshCompletedHandler FirstRefreshCompleted;

        #endregion

        #region Public Properties

        public int Priority
        {
            get { return _priority; }
            set 
            { 
                _priority = value;
                RefreshStatusString();
            }
        }

        public FeedStatus Status
        {
            get { return _status; }
            set { _status = value; }
        }

        public string URL
        {
            get { return _url; }
            set { _url = value; }
        }

        public string Title
        {
            get { return _title; }
            set { _title = value; }
        }

        public string Folder
        {
            get 
            { 
                return Tools.IsValidPath(_folder) ?
                    _folder : Path.Combine(Statics.Config.CompletedFilesBaseDirectory, _folder); 
            }
            set
            {
                if(!Tools.IsValidPath(value))
                {
                    Trace.TraceError("Attempt to set podcast folder to an invalid path. Podcast: {0}, Path: {1}", Title, value);
                    throw new ArgumentException("Attempt to set podcast folder to an invalid path");
                }

                _folder = value;
            }

        }

        /// <summary>
        /// The Items contained in the feed at the last fetch. In the order
        /// they appeared in the feed.
        /// </summary>
        public Item[] Items
        {
            get 
            {
                Item[] items = new Item[_items.Count];
                _items.Values.CopyTo(items, 0);
                return items;
            }
        }

        public ArchiveMode ArchiveMode
        {
            get { return _archiveMode; }
            set { _archiveMode = value; }
        }

        public bool Highlighted
        {
            get { return object.Equals(Font, Statics.BoldFont); }
            set { Font = value ? Statics.BoldFont : Statics.NormalFont; }
        }

        public bool Syncronised
        {
            get { return _syncronise; }
            set
            {
                _syncronise = value;
                RefreshStatusString();
            }
        }

        public bool AddedDynamically
        {
            get { return _addedDynamically; }
            set { _addedDynamically = value; }
        }

        public string AlbumTag
        {
            get { return _albumTag; }
            set { _albumTag = value; }
        }

        public string GenreTag
        {
            get { return _genreTag; }
            set { _genreTag = value; }
        }

        public string ArtistTag
        {
            get { return _artistTag; }
            set { _artistTag = value; }
        }

        public string TrackTitleTag
        {
            get { return _titleTag; }
            set { _titleTag = value; }
        }

        public string ItemFilenamePattern
        {
            get { return _itemFilenamePattern; }
            set { _itemFilenamePattern = value; }
        }

        public bool ApplyAlbumTag
        {
            get { return _applyAlbumTag; }
            set { _applyAlbumTag = value; }
        }

        public bool ApplyGenreTag
        {
            get { return _applyGenreTag; }
            set { _applyGenreTag = value; }
        }

        public bool ApplyArtistTag
        {
            get { return _applyArtistTag; }
            set { _applyArtistTag = value; }
        }

        public bool ApplyTrackTitleTag
        {
            get { return _applyTrackTitleTag; }
            set { _applyTrackTitleTag = value; }
        }

        public bool OverwriteExistingTags
        {
            get { return _overwriteExisingTags; }
            set { _overwriteExisingTags = value; }
        }
	
        #endregion

        #region Refreshing

        public void CancelRefresh()
        {
            if (!_fetcher.IsBusy)
                return;

            _fetcher.CancelFetch();
        }

        /// <summary>
        /// Re-fetches the feed XML from the remote server and updates the status and content of the
        /// Feed object to reflect any changes. 
        /// </summary>
        public void Refresh()
        {
            if (_fetcher.IsBusy || Status == FeedStatus.Refreshing)
                return;

            ChangeStatus(FeedStatus.Refreshing);

            Item downloadingItem = GetDownloadingItem();
            if (downloadingItem != null)
                downloadingItem.StopDownload();

            _fetcher.FetchIfNotBusy(_url, OnFetchCompleted);
          
        }

        private delegate void OnFeedFetchedHandler(FeedRefreshResult status, XmlDocument feed, string errorMsg, string url);

        private void OnFetchCompleted(FeedRefreshResult status, XmlDocument feed, string errorMsg, string url)
        {
            if (Statics.FeedListView.InvokeRequired)
            {
                Statics.FeedListView.Invoke(new OnFeedFetchedHandler(OnFetchCompleted), status, feed, errorMsg, url);
                return;
            }

            switch (status)
            {
                case FeedRefreshResult.Success:

                    _url = url;
                    status = Refresh(feed); // TODO

                    if(_neverRefreshed)
                        AutoAddTags();

                    if (_items.Count == 0)
                        ChangeStatus(FeedStatus.Complete);
                    else ChangeStatus(FeedStatus.Pending);

                    break;

                case FeedRefreshResult.Redirect:

                    ChangeStatus(FeedStatus.Redirecting);
                    _url = url;

                    Refresh();
                    return;

                case FeedRefreshResult.Canceled:

                    if (_items.Count == 0)
                        ChangeStatus(FeedStatus.Complete);
                    else ChangeStatus(FeedStatus.Pending);

                    break;

                case FeedRefreshResult.InvalidData:
                case FeedRefreshResult.UnableToConnect:

                    Error(errorMsg);

                    if (_neverRefreshed)
                    {
                        if (_neverRefreshed && !AddedDynamically)
                            MessageBox.Show(StatusString, "Podcast Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    ChangeStatus(FeedStatus.Error);

                    break;
            }

            if (_neverRefreshed)// && _items.Count > 1)
            {
                if (AddedDynamically)
                {
                    if (Statics.Config.DynamicOPMLJustGetLatest)
                        HideAllButLatest();
                }

                if (FirstRefreshCompleted != null)
                    FirstRefreshCompleted(this, status);
            }

            if (status == FeedRefreshResult.IsOPML)
                return;

            _neverRefreshed = false;

            ToolTipText = ToolTip;

            if (Statics.FeedListView.SelectedItems.Count == 1 && Statics.FeedListView.SelectedItems[0] == this)
                PopulateItemsListView();

            if(status == FeedRefreshResult.Success)
                WritePlaylist();
        }

        protected FeedRefreshResult Refresh(XmlDocument doc)
        {
            if (doc.DocumentElement.Name.ToLower() == "opml")
                return FeedRefreshResult.IsOPML;

            List<string> itemsInCurrentFeed = null; ;

            // which feed type is it?
            string ns = doc.DocumentElement.NamespaceURI;
            switch (ns)
            {
                case AtomNamespace: //"http://www.w3.org/2005/Atom":
                    //
                    // Atom feed
                    //

                    itemsInCurrentFeed = RefreshAtom(doc);

                    break;

                case Rss1Namespace: //"http://purl.org/rss/1.0/":
                case RdfNamespace: //"http://www.w3.org/1999/02/22-rdf-syntax-ns#":
                    //
                    // RSS 1.0
                    //

                    itemsInCurrentFeed = RefreshRSS1(doc);

                    break;

                case XHTML1_0Namespace:

                    // page is html, could colntain a rel link to a feed.

                    XmlNamespaceManager xhtmlNamespace = new XmlNamespaceManager(doc.NameTable);
                    xhtmlNamespace.AddNamespace("xhtml", XHTML1_0Namespace);
                    XmlNode feedUrl = doc.DocumentElement.SelectSingleNode("/xhtml:html/xhtml:head/xhtml:link[@type='application/rss+xml']/@href", xhtmlNamespace);
                    if (feedUrl == null)
                    {
                        Error("Cannot create news feed, feed type not supported.");
                        return FeedRefreshResult.InvalidData;
                    }
                    else
                    {
                        _url = feedUrl.Value;
                        return FeedRefreshResult.Redirect;
                    }

                case "":
                case null:
                default:
                    // No namespace - could be RSS 2.0
                    if (doc.DocumentElement.Name.ToLower() == "rss" &&
                        doc.DocumentElement.Attributes["version"].Value == "2.0")
                    {
                        //
                        // RSS 2.0
                        // 

                        itemsInCurrentFeed = RefreshRSS2(doc);
                    }
                    else
                    {
                        Error("Cannot create news feed, feed type not supported.");
                        return FeedRefreshResult.InvalidData;
                    }

                    break;
            }

            if (_neverRefreshed)
                Folder = Statics.Config.GetDefaultCompleteDestination(this);

            // delete old items.
            // Note: where old items still appear in the feed we cannot delete our record of them or we
            // will download them again at the next refresh, so items that we dont want to show or download but
            // are still in the feed are put in the Deleted status. Once they disappear from the feed we can
            // remove them from our records.
            List<Item> toRemove = new List<Item>();
            foreach (Item item in _items.Values)
            {
                if (ItemIsOld(item, itemsInCurrentFeed))
                    toRemove.Add(item);
            }
            foreach (Item item in toRemove)
            {
                item.DeleteOldItem();

                if (!itemsInCurrentFeed.Contains(item.URL))
                    _items.Remove(item.URL);
            }

            // reset errored items
            foreach (Item item in _items.Values)
            {
                if (item.Status == ItemStatus.Error)
                    item.TryAgain();
            }

            return FeedRefreshResult.Success;
        }

        private bool ItemIsOld(Item item, List<string> itemsInCurrentFeed)
        {
            if (item.Status == ItemStatus.Deleted && !itemsInCurrentFeed.Contains(item.URL))
                return true;

            switch (ArchiveMode)
            {
                case ArchiveMode.Keep:
                default:
                    return false;

                case ArchiveMode.DeleteAfterOneMonth:
                    {
                        if (item.Status != ItemStatus.Complete || item.DownloadedDate == null)
                            return false;

                        TimeSpan age = DateTime.Now - item.DownloadedDate.Value;
                        return age.Days > 28;
                    }
                case ArchiveMode.DeleteAfterOneWeek:
                    {
                        if (item.Status != ItemStatus.Complete || item.DownloadedDate == null)
                            return false;

                        TimeSpan age = DateTime.Now - item.DownloadedDate.Value;
                        return age.Days > 7;
                    }
                case ArchiveMode.MatchFeed:
                    return !itemsInCurrentFeed.Contains(item.URL);

                case ArchiveMode.KeepLatest:
                        foreach (Item other in Items)
                            if (other.PublicationDate > item.PublicationDate)
                                return true;
                        return false;
            }
        }

        /// <summary>
        /// Refreshes feed data from an RSS1.0 or RDF feed.
        /// </summary>
        /// <param name="doc">The XML to parse.</param>
        /// <returns>The URLs of all the items in the feed.</returns>
        protected List<string> RefreshRSS1(XmlDocument doc)
        {
            Clear();

            XmlNamespaceManager namespaces = new XmlNamespaceManager(doc.NameTable);
            namespaces.AddNamespace("rdf", RdfNamespace);
            namespaces.AddNamespace("rss1", Rss1Namespace);

            List<string> itemsInCurrentFeed = new List<string>();

            try
            {
                if (_neverRefreshed) // after first refresh dont overwrite title as user may have chosen theor own title for it.
                {
                    try
                    {
                        string title = doc.SelectSingleNode("/rdf:RDF/rss1:channel/rss1:title", namespaces).InnerText;
                        if (title != "")
                            _title = title;
                        _link = doc.SelectSingleNode("/rdf:RDF/rss1:channel/rss1:link", namespaces).InnerText;
                    }
                    catch (NullReferenceException)
                    {
                        Clear();
                        Error("Incomplete information in feed.");
                        return new List<string>();
                    }
                }

                foreach (XmlNode entry in doc.SelectNodes("/rdf:RDF/rss1:item", namespaces))
                {
                    try
                    {

                        XmlNode urlNode = entry.SelectSingleNode("rss1:link", namespaces);
                        if (urlNode == null)
                            continue;

                        string url = urlNode.InnerText;

                        itemsInCurrentFeed.Add(url);

                        if (_items.ContainsKey(url))
                            // only add new items
                            continue;

                        string title = entry.SelectSingleNode("rss1:title", namespaces).InnerText;

                        // not all items have a description
                        string description = "";
                        XmlNode descriptionNode = entry.SelectSingleNode("rss1:description", namespaces);
                        if (descriptionNode != null)
                            description = descriptionNode.InnerText;

                        DateTime pubDate = DateTime.Now; // RSS1.0 items don't have a publication date.

                        AddItem(title, description, url, pubDate);
                    }
                    catch (NullReferenceException)
                    {
                        string guid = Guid.NewGuid().ToString();
                        Item item = new Item(this, "???", "???", guid, DateTime.Now);
                        item.Error("Incomplete information in feed");
                        _items.Add(item.URL, item);
                    }
                }                
            }
            catch (Exception ex)
            {
                Error(ex);
            }

            return itemsInCurrentFeed;
        }

        /// <summary>
        /// Refreshes feed data from an RSS2.0 feed.
        /// </summary>
        /// <param name="doc">The XML to parse.</param>
        /// <returns>The URLs of all the items in the feed.</returns>
        protected List<string> RefreshRSS2(XmlDocument doc)
        {
            Clear();

            List<string> itemsInCurrentFeed = new List<string>();

            try
            {
                if (_neverRefreshed) // after first refresh dont overwrite title as user may have chosen theor own title for it.
                {
                    try
                    {
                        string title = doc.SelectSingleNode("/rss/channel/title").InnerText;
                        if (title != "")
                            _title = title;
                        _link = doc.SelectSingleNode("/rss/channel/link").InnerText;
                    }
                    catch (NullReferenceException)
                    {
                        Clear();
                        Error("Incomplete information in feed.");
                        return new List<string>();
                    }
                }

                foreach (XmlNode entry in doc.SelectNodes("/rss/channel/item"))
                {
                    try
                    {
                        // Not all feeds include the type.
                        //
                        // XmlNode urlNode = entry.SelectSingleNode("./enclosure[@type='audio/mpeg']/@url");

                        XmlNode urlNode = entry.SelectSingleNode("./enclosure/@url");
                        if (urlNode == null)
                            continue;

                        string url = urlNode.InnerText;

                        itemsInCurrentFeed.Add(url);

                        if (_items.ContainsKey(url))
                            // only add new items
                            continue;

                        string title = entry.SelectSingleNode("title").InnerText;

                        // not all items have a description
                        string description = "";
                        XmlNode descriptionNode = entry.SelectSingleNode("description");
                        if (descriptionNode != null)
                            description = descriptionNode.InnerText;

                        DateTime pubDate = DateTime.Now;
                        XmlNode pubDateNode = entry.SelectSingleNode("pubDate");
                        if(pubDateNode != null)
                        {                            
                            string pubDateStr = pubDateNode.InnerText;
                            if (!RFC822DateTime.TryParse(pubDateStr, out pubDate))
                                pubDate = DateTime.Now;
                        }                        

                        AddItem(title, description, url, pubDate);
                    }
                    catch (NullReferenceException)
                    {
                        string guid = Guid.NewGuid().ToString();
                        Item item = new Item(this, "???", "???", guid, DateTime.Now);
                        item.Error("Incomplete information in feed");
                        _items.Add(item.URL, item);
                    }
                }                
            }
            catch (Exception ex)
            {
                Error(ex);
            }

            return itemsInCurrentFeed;
        }

        /// <summary>
        /// Refreshes feed data from an Atom feed.
        /// </summary>
        /// <param name="doc">The XML to parse.</param>
        /// <returns>The URLs of all the items in the feed.</returns>
        protected List<String> RefreshAtom(XmlDocument doc)
        {
            Clear();

            List<string> itemsInCurrentFeed = new List<string>();

            try
            {
                XmlNamespaceManager namespaces = new XmlNamespaceManager(doc.NameTable);
                namespaces.AddNamespace("atom", AtomNamespace);

                if (_neverRefreshed) // after first refresh dont overwrite title as user may have chosen theor own title for it.
                {
                    try
                    {
                        string title = doc.SelectSingleNode("/atom:feed/atom:title", namespaces).InnerText;
                        if (title != "")
                            _title = title;
                        _link = doc.SelectSingleNode("/atom:feed/atom:link/@href", namespaces).InnerText;
                    }
                    catch (NullReferenceException)
                    {
                        Clear();
                        Error("Incomplete information in feed.");
                        return new List<string>();
                    }
                }

                foreach (XmlNode entry in doc.SelectNodes("/atom:feed/atom:entry", namespaces))
                {
                    try
                    {
                        XmlNode urlNode = entry.SelectSingleNode("./atom:link[@type='audio/mpeg']/@href", namespaces);
                        if (urlNode == null)
                            continue;

                        string url = urlNode.InnerText;

                        itemsInCurrentFeed.Add(url);

                        if (_items.ContainsKey(url))
                            // only add new items
                            continue;

                        string title = entry.SelectSingleNode("atom:title", namespaces).InnerText;
                        string description = entry.SelectSingleNode("atom:summary", namespaces).InnerText;

                        XmlNode publishedNode = entry.SelectSingleNode("atom:published", namespaces);
                        if (publishedNode == null)
                            publishedNode = entry.SelectSingleNode("atom:updated", namespaces);
                        DateTime pubDate = DateTime.Now;
                        if (publishedNode != null)
                            DateTime.TryParse(publishedNode.InnerText, out pubDate);

                        AddItem(title, description, url, pubDate);
                    }
                    catch (NullReferenceException)
                    {
                        Item item = new Item(this, "???", "???", Guid.NewGuid().ToString(), DateTime.Now);
                        item.Error("Incomplete information in feed");
                        _items.Add(item.URL, item);
                    }
                }                
            }
            catch (Exception ex)
            {
                Error(ex);
            }

            return itemsInCurrentFeed;
        }

        private void AutoAddTags()
        {
            _titleTag = "%t";
            _applyTrackTitleTag = true;
            _albumTag = "%p";
            _applyAlbumTag = true;
            _genreTag = "Podcast";
            _applyGenreTag = true;
        }

        /// <summary>
        /// Adds an item to the feed.
        /// </summary>
        /// <param name="item"></param>
        private void AddItem(Item item)
        {
            // get a unique title for the item
            List<string> titles = new List<string>();
            foreach (Item item2 in Items)
                titles.Add(item2.Title);

            string original = item.Title;
            int suffix = 2;
            string current = item.Title;
            while (titles.IndexOf(current) >= 0)
            {
                current = original + " (" + suffix + ")";
                suffix++;
            }
            item.Title = current;

            item.LinkUp(this);
            item.DownloadComplete += new ItemDownloadFinishedHandler(OnItemDownloadComplete);
            item.DownloadStopped += new ItemDownloadFinishedHandler(OnItemDownloadStopped);
            item.StatusChanged += new ItemStatusChangedHandler(OnItemStatusChanged);
            _items.Add(item.URL, item);
        }

        /// <summary>
        /// Executed each time an item belonging to this feed stopped downloaded without completing.
        /// </summary>
        /// <param name="item">The item in question.</param>
        private void OnItemDownloadStopped(Item item)
        {
            if (Status != FeedStatus.Refreshing && Status != FeedStatus.Removed)
            {
                switch (item.Status)
                {

                    case ItemStatus.Error:
                    case ItemStatus.Complete:
                    case ItemStatus.Pending:
                    case ItemStatus.Skip:
                        ChangeStatus(FeedStatus.Pending);
                        break;
                }
            }
        }

        void OnItemStatusChanged(Item item, ItemStatus oldStatus)
        {
            if (oldStatus == ItemStatus.Complete && item.Status == ItemStatus.Skip)
                WritePlaylist();

            if ((_status == FeedStatus.Complete || _status == FeedStatus.CompleteWithErrors) && item.Status == ItemStatus.Pending)
                ChangeStatus(FeedStatus.Pending);
        }

        private void AddItem(string title, string description, string url, DateTime pubDate)
        {
            Item item = new Item(this, title, description, url, pubDate);
            AddItem(item);
        }

        private void DeleteOldItem(Item item)
        {
            _items.Remove(item.URL);
            item.DownloadComplete -= new ItemDownloadFinishedHandler(OnItemDownloadComplete);
            item.StatusChanged -= new ItemStatusChangedHandler(OnItemStatusChanged);
            item.DeleteOldItem();
        }

        private void OnItemDownloadComplete(Item item)
        {
            Highlighted = !Selected;
            WritePlaylist();
            ChangeStatus(FeedStatus.Pending);
            if (ItemDownloaded != null)
                ItemDownloaded(item);
        }

        #endregion        

        #region Public Methods
  
        /// <summary>
        /// Does whatever is neccesary to ensure that data is coherent after a previous session of the app
        /// terminated unexpectedly. Practically this means restarting all partially downloaded items.
        /// </summary>
        public void RecoverFromDirtyShutdown()
        {
            foreach (Item item in _items.Values)
                item.RecoverFromDirtyShutdown();
        }

        /// <summary>
        /// Attempts to begin downloading the next incomplete item in the feed, this will only work if the feed status is pending,
        /// there is at least one pending item in the feed and there is at least one free download worker.
        /// </summary>
        /// <returns>True on success.</returns>
        public bool StartNextDownload()
        {
            if (_status != FeedStatus.Pending)
                return false;

            bool errors = false;

            foreach (Item item in _items.Values)
            {
                if (item.Status == ItemStatus.Pending)
                {
                    ChangeStatus(FeedStatus.Downloading);

                    if (item.Download())
                        return true;
                    else ChangeStatus(FeedStatus.Pending);
                }
                else if (item.Status == ItemStatus.Error)
                    errors = true;
            }

            if (errors)
                ChangeStatus(FeedStatus.CompleteWithErrors);
            else ChangeStatus(FeedStatus.Complete);

            return false;
        }

        /// <summary>
        /// Makes the latest value of the StatusString property visible to the user.
        /// </summary>
        public void RefreshStatusString()
        {
            if (ListView == null)
                return;

            if (ListView.InvokeRequired)
            {
                ItemUpdateHandler h = new ItemUpdateHandler(RefreshStatusString);
                ListView.Invoke(h, new object[] { });
            }
            else
            {
                Text = _title;
                SubItems[1].Text = StatusString;
                SubItems[2].Text = _syncronise ? "●" : "";
                SubItems[3].Text = Priority.ToString();  
                SubItems[4].Text = Tools.ToSensibleTimeString(LatestDownloadTime);                
            }
        }

        /// <summary>
        /// returns the time at which the last item completed downloading or DateTime.Minvalue if no items are complete.
        /// </summary>
        private DateTime LatestDownloadTime
        {
            get
            {
                DateTime latest = DateTime.MinValue;
                foreach (Item item in _items.Values)
                    if (item.Status == ItemStatus.Complete && item.DownloadedDate.HasValue && item.DownloadedDate.Value > latest)
                        latest = item.DownloadedDate.Value;

                return latest;
            }
        }

        /// <summary>
        /// Populates the items view with this feed's items.
        /// </summary>
        public void PopulateItemsListView()
        {
            Statics.ItemListView.BeginUpdate();
            Statics.ItemListView.Items.Clear();
            List<Item> visibleItems = new List<Item>();
            foreach (Item item in _items.Values)
            {
                if (item.Status != ItemStatus.Deleted /* && !(Statics.Config.HideSkippedItems  && item.Status == ItemStatus.Skip)*/)
                    visibleItems.Add(item);
            }
            Item[] itemArr = visibleItems.ToArray();
            Statics.EnableItemCheckBoxes = false;
            Statics.ItemListView.Items.AddRange(itemArr);
            Statics.EnableItemCheckBoxes = true;
            foreach (Item item in itemArr)
            {
                item.RefreshStatusString();
                item.RefreshDateDownloadedString();
                item.RefreshToolTip();
            }
            Statics.ItemListView.EndUpdate();
        }

        public string ToolTip
        {
            get
            {
                if (_neverRefreshed)
                    return "";

                StringBuilder sb = new StringBuilder();

                sb.Append("Title: ");
                sb.Append(Title);
                sb.Append('\n');
                sb.Append("Items: ");
                sb.Append(_items.Count);
                sb.Append('\n');
                sb.Append("URL: ");
                sb.Append(URL);

                return sb.ToString();
            }
        }

        //public override void Remove()
        //{
        //    Remove(true);
        //}

        public void Remove(bool deleteFiles)
        {
            Item downloadingItem = null;
            if (Status == FeedStatus.Downloading)
                downloadingItem = GetDownloadingItem();

            ChangeStatus(FeedStatus.Removed);

            if(downloadingItem != null)
                downloadingItem.StopDownload();

            Statics.ItemListView.BeginUpdate();
            foreach (Item item in _items.Values)
                item.Remove(); // remove from list view if its in there.
            Statics.ItemListView.EndUpdate();

            if (deleteFiles && Directory.Exists(Statics.Config.GetCompleteDestination(this)))
                try
                {
                    Directory.Delete(Statics.Config.GetCompleteDestination(this), true);
                    //FileSystem.DeleteDirectory(Statics.Config.GetCompleteDestination(this), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                catch (UnauthorizedAccessException) { }

            base.Remove();
        }

        public void OpenContainingFolder()
        {
            try
            {
                Process.Start(Statics.Config.GetCompleteDestination(this));
            }
            catch (Exception) { }
        }

        public void Play()
        {
            try
            {
                if(File.Exists(PlaylistFilename))
                    Process.Start(PlaylistFilename);
            }
            catch
            {
            }
        }

        public void HideAllButLatest()
        {
            if (_items.Count <= 1)
                return;

            Item latest = _items[0].Value;
            foreach (Item item in _items.Values)
            {
                if (item.PublicationDate > latest.PublicationDate)
                    latest = item;
            }
            foreach (Item item in _items.Values)
            {
                if (item != latest)
                    item.Hide();
            }
        }

        #region Feed Comparison Methods

        public static int NameComparer(Feed a, Feed b)
        {
            return a.Title.CompareTo(b.Title);
        }

        public static int StatusComparer(Feed a, Feed b)
        {
            return a.StatusString.CompareTo(b.StatusString);
        }

        public static int SyncronisedComparer(Feed a, Feed b)
        {
            return a.Syncronised.CompareTo(b.Syncronised);
        }

        public static int PriorityComparer(Feed a, Feed b)
        {
            return a.Priority.CompareTo(b.Priority);
        }

        public static int LatestItemComparer(Feed a, Feed b)
        {
            return a.LatestDownloadTime.CompareTo(b.LatestDownloadTime);
        }

        #endregion

        #endregion

        #region Protected Methods

        protected void Error(Exception ex)
        {
            Error(ex.Message);
        }

        protected void Error(string message)
        {
            System.Diagnostics.Trace.TraceWarning("Feed Error: " + message + ". URL: " + (_url == null ? "null" : _url));
            _errorMessage = message;
            ChangeStatus(FeedStatus.Error);
        }

        protected void ChangeStatus(FeedStatus newStatus)
        {
            if (newStatus == _status)
                return;

            _status = newStatus;

            RefreshStatusString();

            switch (_status)
            {
                case FeedStatus.Pending:
                case FeedStatus.Complete:
                case FeedStatus.Removed:

                    Statics.DownloadManager.StartNextDownload();

                    break;
            }

            Statics.NotifyIconManager.RefreshMode();
        }        

        protected void Clear()
        {
            _description = "???";
            _errorMessage = "";
            _link = "???";
            //_title = _url;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the Item from this feed that is currently being downloaded or null if none are.
        /// </summary>
        /// <returns></returns>
        public Item GetDownloadingItem()
        {
            foreach (Item item in _items.Values)
            {
                if (item.Status == ItemStatus.Downloading)
                    return item;
            }
            return null;
        }

        /// <summary>
        /// Writes an m3u playlist file representing the items in this feed. The file is written in
        /// the feed's folder.
        /// </summary>
        public void WritePlaylist()
        {
            if (!Statics.Config.WritePlaylists)
                return;

            string playlistFilename = PlaylistFilename;
            
            bool tryAgain = false;
            StreamWriter writer = null;

            do
            {
                try
                {
                    string dir = Path.GetDirectoryName(playlistFilename);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    writer = new StreamWriter(File.Create(playlistFilename));

                    foreach (Item item in _items.Values)
                    {
                        if (item.Status == ItemStatus.Complete)
                        {
                            if(Path.GetDirectoryName(item.CompleteDestination) == Path.GetDirectoryName(playlistFilename))
                                writer.WriteLine(Path.GetFileName(item.CompleteDestination));
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    tryAgain = MessageBox.Show("Unable to write the playlist file '" + playlistFilename + "'. The file is in use.",
                        "Save Playlist", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Problem saving playlist '" + playlistFilename + "'. " + ex.Message, "Save Playlist", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    if (writer != null)
                    {
                        writer.Flush();
                        writer.Close();
                    }
                }
            }
            while (tryAgain);
        }

        #endregion

        #region Private Properties

        /// <summary>
        /// Gets the string that is displayed in the UI representing the current status of the Feed. 
        /// </summary>
        private string StatusString
        {
            get
            {
                switch (_status)
                {
                    case FeedStatus.Complete:
                        return "Up to Date";
                    case FeedStatus.CompleteWithErrors:
                        return "Up to Date (some items failed to download)";
                    case FeedStatus.Downloading:
                        int numToDo = 0;
                        int numComplete = 0;
                        foreach (Item i in _items.Values)
                        {
                            switch (i.Status)
                            {
                                case ItemStatus.Complete:
                                case ItemStatus.Error:
                                    numComplete++;
                                    break;
                                case ItemStatus.Downloading:
                                case ItemStatus.Pending:
                                    numToDo++;
                                    break;
                            }
                        }
                        int total = numToDo + numComplete;
                        numComplete++;
                        string result = "Downloading " + numComplete + " of " + total;
                        Item downloadingItem = GetDownloadingItem();
                        if (downloadingItem != null)
                            result += " (" + downloadingItem.PercentComplete + "%)";
                        return result;
                    case FeedStatus.Pending:
                        
                        return Statics.Paused ? "Paused" : "Pending";
                    case FeedStatus.Refreshing:
                        return "Refreshing";
                    case FeedStatus.Error:
                        return "Error: " + _errorMessage;
                }
                return ""; // for compiler
            }
        }

        /// <summary>
        /// Gets the filename of this feed's playlist file (the file may or may not exist).
        /// </summary>
        public string PlaylistFilename
        {
            get
            {
                return Statics.Config.GetCompleteDestination(this)
                + "\\"
                + Statics.Config.SanitiseFileOrFolderName(Title) + ".m3u";
            }
        }

        #endregion

        #region IXmlSerializable Members

        public System.Xml.Schema.XmlSchema GetSchema()
        {
            // not used
            throw new Exception("The method or operation is not implemented.");
        }

        public void ReadXml(XmlReader reader)
        {
            try
            {
                reader.ReadStartElement();

                if (reader.Name == "rank")
                    reader.ReadElementString();
                //_rank = int.Parse(reader.ReadElementString("rank"));

                _url = reader.ReadElementString("url");
                _link = reader.ReadElementString("link");
                _title = reader.ReadElementString("title");
                Text = _title;
                _description = reader.ReadElementString("description");
                _status = (FeedStatus)Enum.Parse(typeof(FeedStatus), reader.ReadElementString("status"));
                _archiveMode = (ArchiveMode)Enum.Parse(typeof(ArchiveMode), reader.ReadElementString("oldItemAction"));
                _syncronise = bool.Parse(reader.ReadElementString("syncronise"));
                _addedDynamically = bool.Parse(reader.ReadElementString("dynamic"));

                _priority = -1;

                try
                {
                    _albumTag = reader.ReadElementString("albumTag");
                    _genreTag = reader.ReadElementString("genreTag");
                    _artistTag = reader.ReadElementString("artistTag");
                    _titleTag = reader.ReadElementString("titleTag");
                    _applyAlbumTag = bool.Parse(reader.ReadElementString("applyAlbumTag"));
                    _applyArtistTag = bool.Parse(reader.ReadElementString("applyArtistTag"));
                    _applyGenreTag = bool.Parse(reader.ReadElementString("applyGenreTag"));
                    _applyTrackTitleTag = bool.Parse(reader.ReadElementString("applyTrackTitleTag"));
                    _overwriteExisingTags = bool.Parse(reader.ReadElementString("overwriteExistingTags"));
                    _priority = int.Parse(reader.ReadElementString("priority"));
                    _folder = reader.ReadElementString("folder");
                    _itemFilenamePattern = reader.ReadElementString("itemFilenamePattern");
                }
                catch
                {
                    if(_folder == null)
                        _folder = Statics.Config.SanitiseFileOrFolderName(_title);

                    if (_priority == -1)
                    {
                        _priority = _nextPriority;
                        _nextPriority++;
                    }

                    while (reader.Name != "items")
                        reader.Read();
                }

                reader.MoveToAttribute("count");
                int itemCount = reader.ReadContentAsInt();
                reader.ReadStartElement("items");

                XmlSerializer ser = new XmlSerializer(typeof(Item));
                _items = new KeyedList<string, Item>();
                for (int n = 0; n < itemCount; n++)
                {
                    Item item = ser.Deserialize(reader) as Item;
                    AddItem(item);
                }

                if (itemCount > 0)
                    reader.ReadEndElement();

                reader.ReadEndElement();
            }
            catch (Exception ex)
            {
                Error("Deserialisation error: " + ex.Message);
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            //writer.WriteElementString("rank", _rank.ToString());

            writer.WriteElementString("url", _url);
            writer.WriteElementString("link", _link);
            writer.WriteElementString("title", _title);
            writer.WriteElementString("description", _description);
            writer.WriteElementString("status", _status == FeedStatus.Refreshing ? FeedStatus.Pending.ToString() : _status.ToString());
            writer.WriteElementString("oldItemAction", _archiveMode.ToString());
            writer.WriteElementString("syncronise", _syncronise.ToString());
            writer.WriteElementString("dynamic", _addedDynamically.ToString());

            writer.WriteElementString("albumTag", _albumTag);
            writer.WriteElementString("genreTag", _genreTag);
            writer.WriteElementString("artistTag", _artistTag);
            writer.WriteElementString("titleTag", _titleTag);
            writer.WriteElementString("applyAlbumTag", _applyAlbumTag.ToString());
            writer.WriteElementString("applyArtistTag", _applyArtistTag.ToString());
            writer.WriteElementString("applyGenreTag", _applyGenreTag.ToString());
            writer.WriteElementString("applyTrackTitleTag", _applyTrackTitleTag.ToString());
            writer.WriteElementString("overwriteExistingTags", _overwriteExisingTags.ToString());
            writer.WriteElementString("priority", _priority.ToString());
            writer.WriteElementString("folder", _folder);
            writer.WriteElementString("itemFilenamePattern", _itemFilenamePattern);

            writer.WriteStartElement("items");
            writer.WriteAttributeString("count", _items.Count.ToString());
            XmlSerializer ser = new XmlSerializer(typeof(Item));
            foreach (Item item in _items.Values)
            {
                ser.Serialize(writer, item);
            }
            writer.WriteEndElement();
        }

        #endregion
    }
}
