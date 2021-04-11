// Copyright RoSchmi 2019 License Apache 2.0
// Version  24.02.2020 for TinyCLR v2.0.0
// Parts of the code were taken from
// AndyCross: netmfazurestorage / Table / TableClient.cs
// -https://github.com/azure-contrib/netmfazurestorage/blob/master/netmfazurestorage/Table/TableClient.cs
//
// Other parts of the code are taken from martin calsyn
// -https://github.com/PervasiveDigital/serialwifi/tree/master/src/Common

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Security.Cryptography;
//using PervasiveDigital.Utilities;
using RoSchmi.Utilities;
using PervasiveDigital.Security.ManagedProviders;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using GHIElectronics.TinyCLR.Devices.Network;


namespace RoSchmi.Net.Azure.Storage
{
    public class TableClient
    {
        private readonly CloudStorageAccount _account;
        //private string VersionHeader = "2011-08-18";
        //private string VersionHeader = "2015-02-21";
        private string VersionHeader = "2015-04-05";

      //  internal DateTime InstanceDate { get; set; }

        private bool _fiddlerIsAttached = false;
        private IPAddress _fiddlerIP = null;
        private int _fiddlerPort = 8888;
       
        private NetworkController networkController;      

        #region "Debugging"
        private AzureStorageHelper.DebugMode _debug = AzureStorageHelper.DebugMode.StandardDebug;
        private AzureStorageHelper.DebugLevel _debug_level = AzureStorageHelper.DebugLevel.DebugAll;


        private void _Print_Debug(string message)
        {
            switch (_debug)
            {
                //Do nothing
                case AzureStorageHelper.DebugMode.NoDebug:
                    break;

                //Output Debugging info to the serial port
                case AzureStorageHelper.DebugMode.SerialDebug:
                    //Convert the message to bytes
                    /*
                    byte[] message_buffer = System.Text.Encoding.UTF8.GetBytes(message);
                    _debug_port.Write(message_buffer,0,message_buffer.Length);
                    */
                    break;

                //Print message to the standard debug output
                case AzureStorageHelper.DebugMode.StandardDebug:
                    Debug.WriteLine(message);
                    break;
            }
        }
        #endregion


        public enum ContType
        {
            applicationIatomIxml,
            applicationIjson
        }
        public enum AcceptType
        {
            applicationIatomIxml,
            applicationIjson
        }

        public enum ResponseType
        {
            returnContent,
            dont_returnContent
        }

        private string _PartitionKey = "";
        private string _RowKey = "";
        private string _Query = "";

        private string _OperationResponseBody = null;
        private string _OperationResponseMD5 = null;
        private string _OperationResponseETag = null;
        private Hashtable _OperationResponseSingleQuery = null;
        private ArrayList _OperationResponseQueryList = null;

        //Root CA Certificate needed to validate HTTPS servers.
        public static X509Certificate[] caCerts;

        /// <summary>
        /// Set the debugging level.
        /// </summary>
        /// <param name="Debug_Level">The debug level</param>
        public void SetDebugLevel(AzureStorageHelper.DebugLevel Debug_Level)
        {
            this._debug_level = Debug_Level;
        }
        /// <summary>
        /// Set the debugging mode.
        /// </summary>
        /// <param name="Debug_Level">The debug level</param>
        public void SetDebugMode(AzureStorageHelper.DebugMode Debug_Mode)
        {
            this._debug = Debug_Mode;
        }


        #region Accessors for OperationResponse
        public string OperationResponseBody
        { get { return _OperationResponseBody; } }

        public string OperationResponseMD5
        { get { return _OperationResponseMD5; } }

        public string OperationResponseETag
        { get { return _OperationResponseETag; } }

        public Hashtable OperationResponseSingleQuery
        { get { return _OperationResponseSingleQuery; } }

        public ArrayList OperationResponseQueryList
        { get { return _OperationResponseQueryList; } }
        #endregion

        protected byte[] GetBodyBytesAndLength(string body, out int contentLength)
        {
            var content = Encoding.UTF8.GetBytes(body);
            contentLength = content.Length;
            return content;
        }

        protected string GetDateHeader()
        {
            return DateTime.UtcNow.ToString("R");
        }

        #region Constructor
       
        public TableClient(CloudStorageAccount account, X509Certificate[] pCertificat, AzureStorageHelper.DebugMode pDebugMode, AzureStorageHelper.DebugLevel pDebugLevel, NetworkController pNetworkCtr = null)       
        {
            
            networkController = pNetworkCtr;
            _account = account;
            //InstanceDate = DateTime.UtcNow;
            caCerts = pCertificat;
            _debug = pDebugMode;
            _debug_level = pDebugLevel;
        }
        #endregion

        #region private OperationResultsClear
        private void OperationResultsClear()
        {
            _OperationResponseETag = null;
            _OperationResponseBody = null;
            _OperationResponseMD5 = null;
            _OperationResponseSingleQuery = null;
            _OperationResponseQueryList = null;
        }
        #endregion

        #region private getContentTypeString
        private string getContentTypeString(ContType pContentType)
        {
            if (pContentType == ContType.applicationIatomIxml)
            { return "application/atom+xml"; }
            else
            { return "application/json"; }
        }
        #endregion

        #region private getAcceptTypeString
        private string getAcceptTypeString(AcceptType pAcceptType)
        {
            if (pAcceptType == AcceptType.applicationIatomIxml)
            { return "application/atom+xml"; }
            else
            { return "application/json"; }
        }
        #endregion

        #region private getResponseTypeString
        private string getResponseTypeString(ResponseType pResponseType)
        {
            if (pResponseType == ResponseType.returnContent)
            { return "return-content"; }
            else
            { return "return-no-content"; }
        }
        #endregion

        #region public attachFiddler
        public void attachFiddler(bool pfiddlerIsAttached, IPAddress pfiddlerIP, int pfiddlerPort)
        {
            _fiddlerIsAttached = pfiddlerIsAttached;
            _fiddlerIP = pfiddlerIP;
            _fiddlerPort = pfiddlerPort;
        }
        #endregion

        #region CreateTable
        public HttpStatusCode CreateTable(string tableName, ContType pContentType = ContType.applicationIatomIxml, AcceptType pAcceptType = AcceptType.applicationIjson, ResponseType pResponseType = ResponseType.returnContent, bool useSharedKeyLite = false)       
        {
            OperationResultsClear();
            string timestamp = GetDateHeader();

            // RoSchmi, for Debugging
            //timestamp = "2020-09-30T23:31:04";
            String timestampUTC = timestamp + ".0000000Z";

            string content = string.Empty;

            string contentType = getContentTypeString(pContentType);
            string acceptType = getAcceptTypeString(pAcceptType);

            //long totalMemory = GC.GetTotalMemory(true);
        
            content = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>" +
           "<entry xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\"  " +
           "xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\" " +
           "xmlns=\"http://www.w3.org/2005/Atom\"> " +
           "<id>http://" + _account.AccountName + ".table.core.windows.net/Tables('"
               + tableName +
           "')</id>" +
           "<title />" +
           "<updated>" + timestampUTC + "</updated>" +
           "<author><name/></author> " +
           "<content type=\"application/xml\"><m:properties><d:TableName>" + tableName + "</d:TableName></m:properties></content></entry>";

            // "<updated>" + timestampUTC + "</updated>" +
            //"<updated>" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.0000000Z") + "</updated>" +
            string HttpVerb = "POST";
            string ContentMD5 = string.Empty;
            byte[] hashContentMD5 = null;
            int contentLength = 0;

            // RoSchmi for debugging
            // Debug.WriteLine(content);

            byte[] payload = GetBodyBytesAndLength(content, out contentLength);

            content = null; // free memory
           
            string authorizationHeader = CreateTableAuthorizationHeader(payload, String.Format("/{0}/{1}", _account.AccountName, "Tables()"), timestamp, HttpVerb, pContentType, out ContentMD5, out hashContentMD5, useSharedKeyLite = false);
         
            string urlPath = String.Format("{0}", tableName);
           
            string canonicalizedResource = String.Format("/{0}/{1}", _account.AccountName, urlPath);
           
            string canonicalizedHeaders = String.Format("Date:{0}\nx-ms-date:{1}\nx-ms-version:{2}", timestamp, timestamp, VersionHeader);

            string TableEndPoint = _account.UriEndpoints["Table"].ToString();

            Uri uri = new Uri(TableEndPoint + "/Tables()");

            var tableTypeHeaders = new Hashtable();
            tableTypeHeaders.Add("Accept-Charset", "UTF-8");
            tableTypeHeaders.Add("MaxDataServiceVersion", "3.0;NetFx");
            tableTypeHeaders.Add("Content-Type", contentType);
            tableTypeHeaders.Add("DataServiceVersion", "3.0");
            tableTypeHeaders.Add("Prefer", getResponseTypeString(pResponseType));
            tableTypeHeaders.Add("Content-MD5", ContentMD5);

            if (_fiddlerIsAttached)
            { AzureStorageHelper.AttachFiddler(_fiddlerIsAttached, _fiddlerIP, _fiddlerPort); }

            BasicHttpResponse response = new BasicHttpResponse();
            try
            {
                AzureStorageHelper.SetDebugMode(_debug);
                AzureStorageHelper.SetDebugLevel(_debug_level);
               
                response = AzureStorageHelper.SendWebRequest(caCerts, uri, authorizationHeader, timestamp, VersionHeader, payload, contentLength, HttpVerb, false, acceptType, tableTypeHeaders);

                return response.StatusCode;
            }
            catch (Exception ex)
            {
                _Print_Debug("Exception was cought: " + ex.Message);
                //Debug.WriteLine("Exception was cought: " + ex.Message);
                response.StatusCode = HttpStatusCode.Forbidden;
                return response.StatusCode;
            }
        }
        #endregion

        #region InsertTabelEntity
        public HttpStatusCode InsertTableEntity(string tableName, TableEntity pEntity, ContType pContentType = ContType.applicationIatomIxml, AcceptType pAcceptType = AcceptType.applicationIjson, ResponseType pResponseType = ResponseType.returnContent, bool useSharedKeyLite = false)
        {
            OperationResultsClear(); ;
            string timestamp = GetDateHeader();

            //RoSchmi: for tests
            //timestamp = "Wed, 21 Oct 2020 08:53:19 GMT";




            string content = string.Empty;

            string contentType = getContentTypeString(pContentType);
            string acceptType = getAcceptTypeString(pAcceptType);

            //long totalMemory = GC.GetTotalMemory(true);

            switch (contentType)
            {
                case "application/json":
                    {
                        content = pEntity.ReadJson();
                    }
                    break;
                case "application/atom+xml":
                    {
                        content =
                          String.Format("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><entry xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\" xmlns=\"http://www.w3.org/2005/Atom\">" +
            "<id>http://{0}.table.core.windows.net/{5}(PartitionKey='{2}',RowKey='{3}')</id>" +
            "<title/><updated>{1}</updated>" +
            "<author><name /></author>" +
            "<content type=\"application/atom+xml\"><m:properties><d:PartitionKey>{2}</d:PartitionKey><d:RowKey>{3}</d:RowKey>" +
            "{4}" +
            "</m:properties>" +
            "</content>" +
            "</entry>", _account.AccountName, timestamp, pEntity.PartitionKey, pEntity.RowKey, GetTableXml(pEntity.Properties), tableName);

                    }
                    break;
                default:
                    {
                        throw new NotSupportedException("ContentType must be 'application/json' or 'application/atom+xml'");
                    }
            }
            string HttpVerb = "POST";
            int contentLength = 0;
            byte[] payload = GetBodyBytesAndLength(content, out contentLength);
            content = null;     // free memory
            string ContentMD5 = string.Empty;
            byte[] hashContentMD5 = null;

            var authorizationHeader = CreateTableAuthorizationHeader(payload, String.Format("/{0}/{1}", _account.AccountName, tableName + "()"), timestamp, HttpVerb, pContentType, out ContentMD5, out hashContentMD5, useSharedKeyLite);
         
            string urlPath = String.Format("{0}", tableName);
            
            string canonicalizedResource = String.Format("/{0}/{1}", _account.AccountName, urlPath);
           
            string canonicalizedHeaders = String.Format("Date:{0}\nx-ms-date:{1}\nx-ms-version:{2}", timestamp, timestamp, VersionHeader);

            string TableEndPoint = _account.UriEndpoints["Table"].ToString();

            Uri uri = new Uri(TableEndPoint + "/" + tableName + "()");

            var tableTypeHeaders = new Hashtable
            {
                { "Accept-Charset", "UTF-8" },
                { "MaxDataServiceVersion", "3.0;NetFx" },
                { "Content-Type", contentType },
                { "DataServiceVersion", "3.0" },
                { "Prefer", getResponseTypeString(pResponseType) },
                { "Content-MD5", ContentMD5 }
            };

           
            if (_fiddlerIsAttached)
            { AzureStorageHelper.AttachFiddler(_fiddlerIsAttached, _fiddlerIP, _fiddlerPort); }

            BasicHttpResponse response = new BasicHttpResponse();
            try
            {
                AzureStorageHelper.SetDebugMode(_debug);
                AzureStorageHelper.SetDebugLevel(_debug_level);
                
                response = AzureStorageHelper.SendWebRequest(caCerts, uri, authorizationHeader, timestamp, VersionHeader, payload, contentLength, HttpVerb, false, acceptType, tableTypeHeaders);
                _OperationResponseETag = response.ETag;
                _OperationResponseMD5 = response.Content_MD5;
                return response.StatusCode;
            }
            catch (OutOfMemoryException ex1)
            {
                throw new OutOfMemoryException("Exc at 01: " + ex1.Message);
            }
            catch (Exception ex)
            {
                _Print_Debug("Exception was cought: " + ex.Message);
                //Debug.WriteLine("Exception was cought: " + ex.Message);
                response.StatusCode = HttpStatusCode.Forbidden;
                return response.StatusCode;
            }
        }
        #endregion

        #region QueryTableEnities (overloaded)

        public HttpStatusCode QueryTableEntities(string tableName, string partitionKey, string rowKey, string query = "", ContType contentType = ContType.applicationIatomIxml, AcceptType acceptType = AcceptType.applicationIjson, bool useSharedKeyLite = false)
        {
            _Query = query;
            _PartitionKey = partitionKey;
            _RowKey = rowKey;
            return QueryTableEntities(tableName, contentType, acceptType, useSharedKeyLite);
        }

        public HttpStatusCode QueryTableEntities(string tableName, string query = "", ContType contentType = ContType.applicationIatomIxml, AcceptType acceptType = AcceptType.applicationIjson, bool useSharedKeyLite = false)
        {
            _Query = query;
            _PartitionKey = "";
            _RowKey = "";
            return QueryTableEntities(tableName, contentType, acceptType, useSharedKeyLite);
        }

        private HttpStatusCode QueryTableEntities(string tableName, ContType pContentType = ContType.applicationIatomIxml, AcceptType pAcceptType = AcceptType.applicationIjson, bool useSharedKeyLite = false)
        {
            OperationResultsClear();
            string pQuery = _Query;
            string partitionKey = _PartitionKey;
            string rowKey = _RowKey;

            string timestamp = GetDateHeader();
            string content = string.Empty;
            //string queryString = "Tables";

            string contentType = getContentTypeString(pContentType);

            string acceptType = getAcceptTypeString(pAcceptType);
            //if (pAcceptType == AcceptType.applicationIjson)
            //{ acceptType = "application/json;odata=minimalmetadata"; }
            // { acceptType = "application/json;odata=nometadata"; }        
            //{ acceptType = "application/json;odata=fullmetadata"; }


            string HttpVerb = "GET";
            string ContentMD5 = string.Empty;
            byte[] hashContentMD5 = null;

            int contentLength = 0;
            byte[] payload = GetBodyBytesAndLength(content, out contentLength);
            content = null;    // clear memory

            string resourceString = string.Empty;
            string queryString = string.Empty;
        
            if (!String.IsNullOrEmpty(pQuery))
                {
                queryString = "?" + pQuery;
            }

            if ((!String.IsNullOrEmpty(partitionKey) && (!String.IsNullOrEmpty(rowKey))))
            {
                resourceString = String.Format("{1}(PartitionKey='{2}',RowKey='{3}')", _account.AccountName, tableName, partitionKey, rowKey);
            }
            if ((String.IsNullOrEmpty(partitionKey) && (String.IsNullOrEmpty(rowKey))))
            {
                resourceString = String.Format("{1}()", _account.AccountName, tableName, partitionKey, rowKey);
            }

            var authorizationHeader = CreateTableAuthorizationHeader(payload, String.Format("/{0}/{1}", _account.AccountName, resourceString), timestamp, HttpVerb, ContType.applicationIatomIxml, out ContentMD5, out hashContentMD5, useSharedKeyLite);

            string TableEndPoint = _account.UriEndpoints["Table"].ToString();

            Uri uri = new Uri(TableEndPoint + "/" + resourceString + queryString);

            var tableTypeHeaders = new Hashtable
            {
                { "MaxDataServiceVersion", "3.0;NetFx" },
                { "Content-Type", contentType },
                { "DataServiceVersion", "3.0;NetFx" },
                { "Content-MD5", ContentMD5 }               
            };

            if (_fiddlerIsAttached)
            { AzureStorageHelper.AttachFiddler(_fiddlerIsAttached, _fiddlerIP, _fiddlerPort); }

            BasicHttpResponse response = new BasicHttpResponse();
            try
            {
                AzureStorageHelper.SetDebugMode(_debug);
                AzureStorageHelper.SetDebugLevel(_debug_level);
                response = AzureStorageHelper.SendWebRequest(caCerts, uri, authorizationHeader, timestamp, VersionHeader, payload, contentLength, HttpVerb, false, acceptType, tableTypeHeaders);

                /*
                long freeMemory = GHIElectronics.TinyCLR.Native.Memory.ManagedMemory.FreeBytes;
                long totalMemory = GC.GetTotalMemory(true);                          
                Debug.WriteLine("TableClient: QueryTableEntities. Total Memory: " + totalMemory.ToString("N0") + " Free Bytes: " + freeMemory.ToString("N0"));
                */

                ArrayList entities = null;
                if ((response.Body != null) && (response.Body.StartsWith("<?xml")))
                {
                    entities = ParseResponse(response.Body);                  
                    _OperationResponseQueryList = entities;
                }
                    else if ((response.Body != null) && (response.Body.StartsWith("{\"odata.metadata\":")))
                    {
                        throw new NotSupportedException("Json serialization is actually not supported");
                        //response.Body = response.Body.Substring(0, response.Body.Length - 7);
                        //var newInstance = (QueryEntity)JsonConverter.DeserializeObject(response.Body, typeof(QueryEntity), CreateInstance);      
                }
               
                _OperationResponseBody = response.Body.Substring(0, Math.Min(response.Body.Length, 300));   // not more than 300 char

                if (entities.Count == 1)
                {
                    _OperationResponseETag = response.ETag;
                    _OperationResponseSingleQuery = entities[0] as Hashtable;
                }
                

                return response.StatusCode;
            }
            catch (OutOfMemoryException e)
            {
                throw new OutOfMemoryException(e.Message);
            }
            catch (Exception ex)
            {
                //_Print_Debug("Exception was cought: " + ex.Message);
                Debug.WriteLine("Exception was cought: " + ex.Message);
                response.StatusCode = HttpStatusCode.NotFound;
               
                return response.StatusCode;
            }
        }

        #endregion

        #region FormatEntityXml
        private string FormatEntityXml(string tablename, string partitionKey, string rowKey, DateTime timeStamp, Hashtable tableEntityProperties)
        {
            var timestamp = timeStamp.ToString("yyyy-MM-ddTHH:mm:ss.0000000Z");

            string xml =
                String.Format("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><entry xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\" xmlns=\"http://www.w3.org/2005/Atom\">" +
                "<id>http://{0}.table.core.windows.net/{5}(PartitionKey='{2}',RowKey='{3}')</id>" +
                "<title/><updated>{1}</updated><author><name /></author>" +
                "<link />" +
                //"<category term=\"{0}.Tables\" scheme=\"http://schemas.microsoft.com/ado/2007/08/dataservices/scheme\" />" +
                "<content type=\"application/xml\"><m:properties><d:PartitionKey>{2}</d:PartitionKey><d:RowKey>{3}</d:RowKey>" +
                "<d:Timestamp m:type=\"Edm.DateTime\">{1}</d:Timestamp>" +
                "{4}" +
                "</m:properties>" +
                "</content>" +
                "</entry>", _account.AccountName, timestamp, partitionKey, rowKey, GetTableXml(tableEntityProperties), tablename);
            return xml;
        }
        #endregion

        #region GetTableXml
        private string GetTableXml(ArrayList tableEntityProperties)
        {
            string result = string.Empty;
            string prop = string.Empty;
            string key = string.Empty;
            for (int i = 0; i < tableEntityProperties.Count; i++)
            {                
                //key = ((string[])tableEntityProperties[i])[1];
                //if ((key != "PartitionKey") && (key != "RowKey"))   // Skip PartitionKey and RowKey
                //{ 
                prop = ((string[])tableEntityProperties[i])[0];
                if (prop != null)
                {
                    result += prop.ToString();
                }
                //}
            }

            return result;
        }


        private static string GetTableXml(Hashtable tableEntityProperties)
        {
            string result = string.Empty;
            foreach (var key in tableEntityProperties.Keys)
            {
                var value = tableEntityProperties[key];
                if (value == null) continue;
                var type = value.GetType().Name;
                switch (type)
                {
                    case "DateTime":
                        value = ((DateTime)value).ToString("yyyy-MM-ddTHH:mm:ss.0000000Z");
                        break;
                    case "Boolean":
                        value = (Boolean)value ? "true" : "false"; // bool is title case when you call ToString()
                        break;
                }
                result += String.Format("<d:{0} m:type=\"Edm.{2}\">{1}</d:{0}>", key, value, type);
            }
            return result;
        }
        #endregion

        #region ParseResponse
        private ArrayList ParseResponse(string xml)
        {
            var results = new ArrayList();
            string entityToken = null;
            var nextStart = 0;
            while (null != (entityToken = NextToken(xml, "<m:properties>", "</m:properties>", nextStart, out nextStart)))
            {
                var currentObject = new Hashtable();
                string propertyToken = null;
                int nextPropertyStart = 0;
                while (null != (propertyToken = NextToken(entityToken, "<d:", "</d", nextPropertyStart, out nextPropertyStart)))
                {
                    var parts = propertyToken.Split('>');
                    if (parts.Length != 2) continue;
                    var rawvalue = parts[1];
                    var propertyName = parts[0].Split(' ')[0];

                    var _ = 0;
                    var type = NextToken(propertyToken, "m:type=\"", "\"", 0, out _);
                    if (null == type)
                    {
                        type = "Edm.String";
                    }
                    if (currentObject.Contains(propertyName)) continue;
                    switch (type)
                    {
                        case "Edm.String":
                            currentObject.Add(propertyName, rawvalue);
                            break;
                        case "Edm.DateTime":
                            // not supported
                            break;
                        case "Edm.Int64":
                            currentObject.Add(propertyName, Int64.Parse(rawvalue));
                            break;
                        case "Edm.Int32":
                            currentObject.Add(propertyName, Int32.Parse(rawvalue));
                            break;
                        case "Edm.Double":
                            currentObject.Add(propertyName, Double.Parse(rawvalue));
                            break;
                        case "Edm.Boolean":
                            currentObject.Add(propertyName, rawvalue == "true");
                            break;
                        case "Edm.Guid":
                            // not supported
                            break;
                    }
                }
                results.Add(currentObject);
            }
            return results;
        }

        private string NextToken(string xml, string startToken, string endToken, int startPosition, out int nextStart)
        {
            if (startPosition > xml.Length)
            {
                nextStart = xml.Length;
                return null;
            }
            var start = xml.IndexOf(startToken, startPosition);
            nextStart = 0;
            if (start < 0) return null;
            start += startToken.Length;
            var end = xml.IndexOf(endToken, start);
            if (end < 0) return null;
            nextStart = end + endToken.Length;
            return xml.Substring(start, end - start);
        }
        #endregion

        #region Shared Access Signature
        /*
        private string MD5ComputeHash(byte[] data)
        {
            
            if (data.Length != 0)
            {               
                wifi.CreateRamFile("fileToHash", data);
                return Encoding.UTF8.GetString(wifi.ComputeHash("3", "fileToHash")).Substring(4);
            }
            else
            {
                return "D41D8CD98F00B204E9800998ECF8427E";
            }

            
            //byte[] hash= xBrainLab.Security.Cryptography.MD5.GetHash(StringData);
            //string hashString = xBrainLab.Security.Cryptography.MD5.GetHashString(StringData);
            

            //using (HashAlgorithm csp = new HashAlgorithm(PervasiveDigital.Security.ManagedProviders.HashAlgorithmType.MD5))
            //using (HashAlgorithm csp = new HashAlgorithm(xBrainLab.Security.Cryptography.MD5)

            //using (HashAlgorithm csp = HashAlgorithm.Create("Md5"))
            
            //using (HashAlgorithm csp = HashAlgorithm.Create("MD5"))
            //{

               // hash = csp.ComputeHash(data);
            //}
            
            //string hashString = ByteExtensions.ToHexString(hash, "");

            //return hashString;
        }
        */    
        #endregion

        #region CreateTableAuthorizationHeader
        protected string CreateTableAuthorizationHeader(byte[] content, string canonicalResource, string ptimeStamp, string pHttpVerb, ContType pContentType, out string pMD5Hash, out byte[] pHash, bool useSharedKeyLite = false)
        {
            string contentType = getContentTypeString(pContentType);
            pMD5Hash = string.Empty;
            pHash = null;
            
            if (!useSharedKeyLite)
            {               
                // long startTime = DateTime.Now.Ticks;
                
                pHash = xBrainLab.Security.Cryptography.MD5.GetHash(content);
                pMD5Hash = BitConverter.ToString(pHash);
                pMD5Hash = pMD5Hash.Replace("-", string.Empty);

                // long endTime = DateTime.Now.Ticks;
                // Debug.WriteLine("Needed for MD5-hash (1): " + ((endTime - startTime) / TimeSpan.TicksPerMillisecond).ToString());  // about 80 ms

                
            }

            string toSign = string.Empty;
            if (useSharedKeyLite)
            {
                toSign = String.Format("{0}\n{1}", ptimeStamp, canonicalResource);
            }
            else
            {
                toSign = String.Format("{0}\n{4}\n{1}\n{2}\n{3}", pHttpVerb, contentType, ptimeStamp, canonicalResource, pMD5Hash);
            }

            string signature;

            #region Region: Tests to use SPWF04SA for SHA256 encoding (not used)
            //toSign = @"POST\n56487EFE04B9981AB97DE7D20353F298\napplication/atom+xml\nTue, 29 Jan 2019 22:38:48 GMT\n/roschmi01/Tables()";

            /*
            wifi.CreateRamFile("fileSHA256", Convert.FromBase64String(_account.AccountKey));
            string strSHA256Hash = Encoding.UTF8.GetString(Program.wifi.ComputeHash("2", "fileSHA256")).Substring(7);
            byte[] theHmac = Convert.FromBase64String(strSHA256Hash);
            */
            //var theHmac = wifi.ComputeHash("2", "fileSHA256");
            #endregion

            //long startTime = DateTime.Now.Ticks;

            //RoSchmi debugging
            //byte[] decodedKey = Convert.FromBase64String(_account.AccountKey);
            
            var hmac = new PervasiveDigital.Security.ManagedProviders.HMACSHA256(Convert.FromBase64String(_account.AccountKey));
            var hmacBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
            signature = Convert.ToBase64String(hmacBytes).Replace("!", "+").Replace("*", "/");
            
            /*
            var hmac = new PervasiveDigital.Security.ManagedProviders.HMACSHA256(Convert.FromBase64String(_account.AccountKey));
            var hmacBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("Roland"));
            signature = Convert.ToBase64String(hmacBytes).Replace("!", "+").Replace("*", "/");
            */


            // long endTime = DateTime.Now.Ticks;
            // Debug.WriteLine("Needed for MD5SHA256-hash: " + ((endTime - startTime) / TimeSpan.TicksPerMillisecond).ToString());  // about 160 ms



            if (useSharedKeyLite)
            {
                return "SharedKeyLite " + _account.AccountName + ":" + signature;
            }
            else
            {
                return "SharedKey " + _account.AccountName + ":" + signature;
            }
        }
        #endregion
      
    }
}

