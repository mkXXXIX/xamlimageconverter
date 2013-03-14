﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Reflection;
using System.Web;

namespace Silversite.Web {

	public class XamlBuildCheck {

		[NonSerialized]
		public static XNamespace xic = "http://schemas.johnshope.com/XamlImageConverter/2012";
		public static XNamespace sb = "http://www.chriscavanagh.com/SkinBuilder";
		public static XNamespace ns;

		public DateTime nsVersion = DateTime.MinValue, Version = DateTime.MinValue;
		public string Application { get { return Context.Request.PhysicalApplicationPath; } }
		public Stack<string> OutputPaths = new Stack<string>();
		public string Culture = "";
		public System.Web.HttpContext Context;
		public int? hash;

		public string OutFilename(string file) {
			if (!file.Contains(":") && !file.Contains("~")) {
				string path = "";
				foreach (var p in OutputPaths) {
					path = Path.Combine(p.Replace('/', '\\'), path);
					if (path.Length > 0 && path[0] == '~') return new FileInfo(Path.Combine(Application, path.Substring(1), file.Replace('/', '\\'))).FullName;
				}
				return InFilename(Path.Combine(path, file));
			} else return InFilename(file);
		}
		public string InFilename(string file) {
			if (!file.Contains(":") && !file.Contains("~")) {
				if (file.StartsWith("/")) file = "~/" + file.Substring(System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath.Length);
				else file = Path.Combine(Path.GetDirectoryName(Context.Request.AppRelativeCurrentExecutionFilePath.Replace('/','\\')), file);
			}
			file = file.Replace('/', '\\').Replace("~\\", Application);
			return new FileInfo(file).FullName;
		}
		public FileInfo Last = null;
		public FileInfo InInfo(string file) {
				return Last = new FileInfo(InFilename(file));
		}
		public FileInfo OutInfo(string file) { return Last = new FileInfo(OutFilename(file)); }

		public bool InFile(string file) {
			if (file.StartsWith("http://") || file.StartsWith("https://")) return true;
			var info = InInfo(file);
			if (info.Exists) {
				if (info.LastWriteTimeUtc > Version) Version = info.LastWriteTimeUtc;
				return true;
			}
			return false;
		}
		public bool OutFile(string file, string culture = null) {
			if (culture != null || string.IsNullOrEmpty(Culture)) {
				file = Path.ChangeExtension(file, culture + Path.GetExtension(file));
				var info = OutInfo(file);
				return (!info.Exists || info.LastWriteTimeUtc < Version);
			} else {
				return Culture.Split(',', ';')
					.Select(c => c.Trim())
					.Where(c => !string.IsNullOrEmpty(c))
					.Any(c => OutFile(file, c));
			}
		}

		public class Group : IDisposable {
			public string OldCulture;
			public void Pop() { if (FBC.OutputPaths.Count > 0) FBC.OutputPaths.Pop(); }
			public void Dispose() { Pop(); FBC.Culture = OldCulture; }
			XamlBuildCheck FBC;
			public Group(XamlBuildCheck fbc, string culture) {
				FBC = fbc;
				OldCulture = fbc.Culture;
				fbc.Culture = culture;
			}
		}

		public Group Open(XElement x) {
			if (x.Attribute("OutputPath") != null) OutputPaths.Push((string)x.Attribute("OutputPath"));
			else OutputPaths.Push("");
			return new Group(this, (string)x.Attribute("Cultures"));
		}

		public Dictionary<string, string> QueryString {
			get {
				var query = HttpContext.Current.Request.QueryString;
				var dict = new Dictionary<string, string>();
				foreach (string key in query.Keys) {
					if (key != null) dict.Add(key, query[key]);
				}
				var exts = query.GetValues(null);
				if (exts != null && exts.Length > 0) dict.Add("Type", exts[0]);
				return dict;
			}
		}

		public bool NeedsBuilding() {
			var file = HttpContext.Current.Request.AppRelativeCurrentExecutionFilePath;
			var ext = Path.GetExtension(file).ToLower();

			if (ext == ".axd") {
				return Doc(CreateAxd(QueryString));
			} else if (ext == ".xaml" || ext == ".psd" || ext == ".svg" || ext == ".svgz") {
				if (!InFile(file)) return false;
				nsVersion = Version;
				return Doc(CreateDirect(file, QueryString));
			} else return Doc(CreateDirect(System.IO.Path.GetFileNameWithoutExtension(file), QueryString));
		}

		bool DocFile(string filename) {
			if (!InFile(filename)) return false;
			nsVersion = Version;
			var doc = XElement.Load(Last.FullName, LoadOptions.None);
			using (Open(doc)) return Doc(doc);
		}

		bool Doc(XElement doc) {
			if (doc == Dynamic) return DynamicResult;
			ns = doc.Name.Namespace;
			if (doc.Name != xic+"XamlImageConverter" && doc.Name != sb+"SkinBuilder") return false;
			using (Open(doc)) return doc.Elements().Any(scene => ParseScene(scene));
		}

		bool ParseScene(XElement x) {
			if (x.Name != ns+"Scene") return false;
			using (Open(x)) {
				Version = nsVersion;
				string source = null;
				var xaml = x.Elements(ns+"Xaml").SingleOrDefault();
				xaml = x.Elements(ns+"Source").SingleOrDefault() ?? xaml;
				if (x.Attribute("Source") != null || x.Attribute("File") != null || x.Attribute("Type") != null) xaml = x;
				var srcAttr = xaml.Attribute("Source") ?? xaml.Attribute("File");
				if (srcAttr != null) source = srcAttr.Value;

				if (xaml == null && source == null) return false;
				if (x.Attribute("Dynamic") != null && InFile(source)) return true;

				return x.Elements().Any(s => ParseSteps(s, source));
			}
		}

		bool ParseSteps(XElement x, string source) {
			using (Open(x)) {
				var snapshot = x.Name == ns+"Snapshot";
				var groupOrSnapshot = x.Name == ns+"Group" || snapshot;
				var map = x.Name == ns+"Map" || x.Name == ns+"ImageMap";
				var par = x.Name == ns+"Set" || x.Name == ns+"Undo" || x.Name == ns+"Reset";
				if (!(groupOrSnapshot || map || par)) return false;
				var file = (string)(x.Attribute("File") ?? x.Attribute("Filename") ?? x.Attribute("Image"));
				if (snapshot && file == null) {
					var type = x.Attribute("Type");
					if (type != null) file = source + "." + type.Value;
				}
				if (snapshot && OutFile(file)) return true;
				if (groupOrSnapshot) return x.Elements().Any(s => ParseSteps(s, source));
				return false;
			}
		}

		public XElement Dynamic = new XElement(xic+"Dynamic");
		public bool DynamicResult;

		public void ApplyParameters(string filename, XElement e, Dictionary<string, string> parameters) {
			var validAttributes = new string[] { "Element", "Storyboard", "Frames", "Filmstrip", "Dpi", "RenderDpi", "Quality", "Filename", "Left", "Top", "Right", "Bottom", "Width", "Height", "Cultures", "RenderTimeout", "Page", "FitToPage", "File", "Loop", "Pause", "Skin", "Theme", "Type", "Image" };

			var type = "png";
			int h = 0;
			bool addhash = false, nohash = false;

			foreach (var key in parameters.Keys.ToList()) {
				h += Hash.Compute(key + "=" + parameters[key]);
				if (validAttributes.Any(a => a == key)) {
					if (key == "Image" || key == "File" || key == "Filename") nohash = true;
					else if (key == "Type") type = parameters["Type"];
					e.SetAttributeValue(key, parameters[key]);
					parameters.Remove(key);
					addhash = !nohash;
				}
			}
			if (addhash) {
				hash = h;
				e.SetAttributeValue("Hash", h);
			}

			foreach (var key in parameters.Keys.ToList()) {
				if (validAttributes.Any(a => a == key)) {
					if (key == "Image" || key == "File" || key == "Filename") nohash = true;
					else if (key == "Type") type = parameters["Type"];
					e.SetAttributeValue(key, parameters[key]);
					parameters.Remove(key);
				}
			}
			if (parameters.Count > 0 && !nohash) {
				hash = 0;
				foreach (var key in parameters.Keys) {
					hash += Hash.Compute(key + "=" + parameters[key]);
				}
				e.SetAttributeValue("Hash", hash.Value);
			}
			if (!nohash && filename != null) {
				e.SetAttributeValue("File", filename + "." + type);
			}
		}

		public XElement CreateDirect(string filename, Dictionary<string, string> parameters) {
			if (filename.EndsWith(".xic.xaml")) return XElement.Load(InFilename(filename), LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo);
			XElement snapshot;
			var res = new XElement(xic+"XamlImageConverter",
					new XElement(xic+"Scene",
						new XAttribute("Source", filename),
						snapshot = new XElement(xic+"Snapshot")
					)
				);
			ApplyParameters(null, snapshot, parameters);
			DynamicResult = true;
			if (parameters.Count > 0) return Dynamic;
			return res;
		}

		public XElement CreateDirect(string filename, XElement e, Dictionary<string, string> parameters) {
			XElement scene;
			if (e.Name == xic+"XamlImageConverter") return e;
			var res = new XElement(xic+"XamlImageConverter",
					scene = new XElement(xic+"Scene", new XElement(xic+"Xaml", e))
				);
			if (parameters.ContainsKey("Image") || parameters.ContainsKey("File") || parameters.ContainsKey("Filename") || parameters.ContainsKey("Type")) {
				var snapshot = new XElement(xic+"Snapshot");
				ApplyParameters(filename, snapshot, parameters);
				scene.Add(snapshot);
			}
			DynamicResult = true;
			if (parameters.Count > 0) return Dynamic;
			return res;
		}

		public XElement CreateAxd(Dictionary<string, string> par) {
			var src = par["Source"];
			if (string.IsNullOrEmpty(src)) {
				DynamicResult = false;
				return Dynamic;
			}
			if (src.Trim()[0] == '#') src = (string)HttpContext.Current.Session["XamlImageConverter.Xaml:" + src];
			if (!src.Trim().StartsWith("<")) return CreateDirect(src, par);
			return CreateDirect("~/Images/Cache/" + Hash.Compute(src).ToString("X"), XElement.Parse(src, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo), par);
		}
	}

	public class Cache {

		public static string Path { get; set; }

		static XNamespace ns = XamlBuildCheck.ns;
		static XNamespace xic = XamlBuildCheck.xic;

		public static string File(string id, XElement xaml, string type = null, string path = null) {
			var e = xaml.DescendantsAndSelf(xic + id).FirstOrDefault();
			if (e != null) {
				if (e.Attribute(xic+"ImageType") != null) type = (string)e.Attribute(xic+"ImageType");
				if (e.Attribute(xic+"Cache") != null) path = (string)e.Attribute(xic+"Cache");
			}
			path = path ?? Path;
			if (type == null) type = "png";
			if (!path.EndsWith("/")) path += "/";
			return path + id + "." + Hash.Compute(xaml).ToString() + "." + type;
		}
	}

#if !Silversite
	public class Hash {

		public static int Compute(byte[] data) {
			unchecked {
				const int p = 16777619;
				int hash = (int)2166136261;

				for (int i = 0; i < data.Length; i++)
					hash = (hash ^ data[i]) * p;

				hash += hash << 13;
				hash ^= hash >> 7;
				hash += hash << 3;
				hash ^= hash >> 17;
				hash += hash << 5;
				hash &= 0x7FFFFFFF;
				return hash;
			}
		}

		public static int Compute(string s) { return Compute(System.Text.Encoding.UTF8.GetBytes(s)); }

		public static int Compute(XElement e) {
			using (var m = new MemoryStream())
			using (var w = XmlWriter.Create(m, new XmlWriterSettings() { NewLineChars = "", Encoding = Encoding.UTF8, Indent = false })) {
				e.Save(w);
				return Compute(m.GetBuffer());
			}
		}
	}

#else
			public class Hash: Silversite.Services.Hash {
			}
#endif

#if Silversite
	[Configuration.ConfigurationSection(Name = "XamlImageConverter")]
	public class Configuration : Silversite.Configuration.Section {
		[ConfigurationProperty("UseService", IsRequired = false, DefaultValue = false)]
		public bool UseSevice { get { return (bool)(this["UseService"] ?? true); } set { this["UseService"] = value; } }
		[ConfigurationProperty("Log", IsRequired = false, DefaultValue = true)]
		public bool Log { get { return (bool)(this["Log"] ?? true); } set { this["Log"] = value; } }
	}
#endif

	public class XamlImageHandler : System.Web.IHttpHandler {

#if Silversite
		public static Configuration Configuration = new Configuration();
#endif

		public bool IsReusable { get { return true; } }

		static System.Web.IHttpHandler handler = null;

		public void ProcessRequest(System.Web.HttpContext context) {
			
			var checker = new XamlBuildCheck() { Context = context };
			
			if (handler != null || checker.NeedsBuilding()) {
				if (handler == null) {
					try {
#if Silversite
						var handlerInfo = Services.Lazy.Types.Info("XamlImageConverter.XamlImageHandler");
						handlerInfo.Load();
						handler = handlerInfo.New();
#else

						var a = Assembly.LoadFrom(context.Server.MapPath("~/Bin/Lazy/XamlImageConverter.dll"));
						var type = a.GetType("XamlImageConverter.XamlImageHandler");
						handler = (System.Web.IHttpHandler)Activator.CreateInstance(type);
#endif
					} catch {
						context.Response.StatusCode = 500;
						context.Response.End();
					}
				}
#if Silversite
				context.Application.Lock();
				context.Application["XamlImageConverter.Configuration.UseService"] = Configuration.UseService;
				context.Application["XamlImageConverter.Configuration.Log"] = Configuration.Log;
				context.Application.UnLock();
#endif
				handler.ProcessRequest(context);
			} else {
				var filename = context.Request.AppRelativeCurrentExecutionFilePath;
				var image = context.Request.QueryString["Image"] ?? context.Request.QueryString["File"] ?? context.Request.QueryString["Filename"];
				var ext = Path.GetExtension(filename).ToLower();
				if (ext == ".xaml" || ext == ".psd" || ext == ".svg" || ext == ".svgz") {
					var exts = context.Request.QueryString.GetValues(null);
					if (exts != null && exts.Length > 0) {
						ext = exts[0];
						image = image ?? filename + "." + ext;
					}
				}
				var name = System.IO.Path.GetFileName(image);
				image = context.Server.MapPath(image);
				ext = System.IO.Path.GetExtension(image);
				if (!string.IsNullOrEmpty(ext)) ext = ext.Substring(1).ToLower();

				switch (ext) {
					case "bmp": context.Response.ContentType = "image/bmp"; break;
					case "png": context.Response.ContentType = "image/png"; break;
					case "jpg":
					case "jpeg":	context.Response.ContentType = "image/jpeg"; break;
					case "tif":
					case "tiff": context.Response.ContentType = "image/tiff"; break;
					case "gif": context.Response.ContentType = "image/gif"; break;
					case "wdp": context.Response.ContentType = "image/vnd.ms-photo"; break;
					case "pdf": 
						context.Response.AppendHeader("content-disposition", "attachment; filename=" + name);
						context.Response.ContentType = "application/pdf"; break;
					case "xps":
						context.Response.AppendHeader("content-disposition", "attachment; filename=" + name);
						context.Response.ContentType = "application/vnd.ms-xpsdocument"; break;
					case "eps":
					case "ps":
						context.Response.AppendHeader("content-disposition", "attachment; filename=" + name);
						context.Response.ContentType = "application/postscript"; break;
					case "psd":
						context.Response.AppendHeader("content-disposition", "attachment; filename=" + name);
						context.Response.ContentType = "image/photoshop"; break;
					case "svg": 
					case "svgz": context.Response.ContentType = "image/svg+xml"; break;
					case "xaml": context.Response.ContentType = "application/xaml+xml"; break;
					default: break;
				}

				if (System.IO.File.Exists(image)) context.Response.WriteFile(image);
				else {
					context.Response.StatusCode = 404;
				}
				context.Response.End();
			}
		}
	}
}