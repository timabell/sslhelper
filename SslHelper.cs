// http://www.pluralsight.com/community/blogs/keith/archive/2009/01/17/sslhelper-get-help-running-a-partial-ssl-website-in-asp-net.aspx
// "Feel free to use it in your own projects without worry about licensing.
//  If you'd attribute the original work to Dominick and myself as in the
//  comments below, I would appreciate it. The usual caveats apply - use at your
//  own risk!"

// This code was originally written by
// Dominick Baier (www.leastprivilege.com)
// His class was called SslTools.
// I made several improvements and renamed it SslHelper.
//   - Keith Brown, http://www.pluralsight.com/keith/

// Further work by Tim Abell, 2009

using System;
using System.Configuration;
using System.Web;

namespace Pluralsight.Utilities
{
	/// <summary>
	/// Class to aid the transition of asp.net pages in and out of ssl.
	/// Use of ssl can be disabled to ease testing by setting the app 
	/// config key EnableSSL to false.
	/// http://github.com/timabell/sslhelper
	/// http://www.pluralsight.com/community/blogs/keith/archive/2009/01/17/sslhelper-get-help-running-a-partial-ssl-website-in-asp-net.aspx
	/// </summary>
	public class SslHelper
	{
		public interface IApplication
		{
			void Redirect(Uri uri);
			Uri CurrentRequestUri { get; }
			bool IsCurrentRequestUsingSsl { get; }
			Uri ResolvePossibleAppRelativeUri(Uri uri);
		}

		// default behavior uses HttpContext.Current
		// implement your own if you want to test
		// outside of ASP.NET
		public class DefaultApplication : IApplication
		{
			public void Redirect(Uri uri) {
				HttpContext.Current
				.Response.Redirect(
					uri.ToString());
			}
			public Uri CurrentRequestUri { get { 
				return HttpContext.Current
					.Request.Url;
			} }
			public bool IsCurrentRequestUsingSsl { get { 
				return HttpContext.Current
					.Request.IsSecureConnection;
			} }
			public Uri ResolvePossibleAppRelativeUri(
				Uri possibleAppRelativeUri)
			{
				// can't be app relative if it's an absolute URI
				if (possibleAppRelativeUri.IsAbsoluteUri)
					return possibleAppRelativeUri;

				string queryString;
				string urlWithoutQueryString =
					SslHelper.StripQueryStringFromRelativeUrl(
						possibleAppRelativeUri, out queryString);

				if (VirtualPathUtility.IsAppRelative(
						urlWithoutQueryString)) {
					string absolutePath =
						VirtualPathUtility.ToAbsolute(
							urlWithoutQueryString);
					Uri currentRequest =
						HttpContext.Current.Request.Url;
					UriBuilder builder = new UriBuilder(
						currentRequest.Scheme,
						currentRequest.Host,
						currentRequest.Port,
						absolutePath);
				
					// using the builder to build the entire URI
					// is problematic when the query string contains
					// URL encoded chars like & because the builder
					// unescapes them. So the following doesn't work:
					//	   builder.Query = queryString;

					if (string.IsNullOrEmpty(queryString))
						return builder.Uri;
					else
					{
						string uriWithoutQuery =
							builder.Uri.AbsoluteUri;
						string absoluteUri = string.Format(
							"{0}?{1}", uriWithoutQuery,
										 queryString);

						return new Uri(absoluteUri,
							UriKind.Absolute);
					}
				}
				else return possibleAppRelativeUri;
			}

		}

		IApplication application;
		IApplication Application { get { return application; } }

		public SslHelper(IApplication application) {
			this.application = application;
		}

		public SslHelper() : this(new DefaultApplication()) { }

		public void Redirect(Uri uri) {
			Redirect(uri, RedirectOptions.Relative);
		}

		public void Redirect(Uri uri, RedirectOptions options) {
			if (options == RedirectOptions.Relative) {
				Application.Redirect(uri);
				return;
			}

			Uri absolutePath = null;
			if (options == RedirectOptions.AbsoluteHttp) {
				absolutePath = GetAbsoluteUri(uri,
					ProtocolOptions.Http);
			}

			if (options == RedirectOptions.AbsoluteHttps) {
				absolutePath = GetAbsoluteUri(uri,
					ProtocolOptions.Https);
			}

			Application.Redirect(absolutePath);
		}

		public Uri GetAbsoluteUri(
				Uri uriFromCaller,
				ProtocolOptions protocol) {
			// you can turn off SSL when testing
			if (ProtocolOptions.Https == protocol &&
					!SslIsEnabledInConfig())
				protocol = ProtocolOptions.Http;

			// deal with ~ (ASP.NET "AppRelative" paths)
			if (!uriFromCaller.IsAbsoluteUri)
				uriFromCaller = Application
					.ResolvePossibleAppRelativeUri(uriFromCaller);

			if (uriFromCaller.IsAbsoluteUri) {
				UriBuilder builder = new UriBuilder(
					SchemeFor(protocol), uriFromCaller.Host);
				PreservePortIfPossible(builder, uriFromCaller);
				builder.Path = uriFromCaller.GetComponents(
					UriComponents.Path, UriFormat.Unescaped);

				// UriBuilder.Query unescapes escaped query strings,
				// which hoses me for stuff like ReturnUrl
				// so I'm doing that part manually
				//   builder.Query = uriFromCaller.GetComponents(
				//	 UriComponents.Query, UriFormat.UriEscaped);

				string query = uriFromCaller.GetComponents(
					UriComponents.Query, UriFormat.UriEscaped);
				if (query.Length > 0) {
					string uriWithoutQuery = builder.Uri.AbsoluteUri;
					string absoluteUri = string.Format("{0}?{1}",
						uriWithoutQuery, query);
					return new Uri(absoluteUri, UriKind.Absolute);
				}
				else return builder.Uri;
			}
			else { // relative URI
				Uri currentRequestUri = Application.CurrentRequestUri;

				UriBuilder builder = new UriBuilder(
					SchemeFor(protocol), currentRequestUri.Host);
				PreservePortIfPossible(builder, currentRequestUri);
				builder.Path = currentRequestUri.GetComponents(
					UriComponents.Path, UriFormat.Unescaped);
				return new Uri(builder.Uri, uriFromCaller);
			}
		}

		private static void PreservePortIfPossible(
				UriBuilder builder, Uri originalUri) {
			// if scheme is unchanging, preserve the port,
			// otherwise use default port
			// this is helpful if you're running this in
			// Visual Studio under some random port with SSL disabled
			if (originalUri.Scheme.Equals(builder.Scheme))
				builder.Port = originalUri.Port;
		}

		private static string SchemeFor(ProtocolOptions protocol) {
			return ProtocolOptions.Https == protocol ?
				Uri.UriSchemeHttps : Uri.UriSchemeHttp;
		}

		public void SwitchToSsl() {
			if (SslIsEnabledInConfig())
				Redirect(new Uri(
						Application.CurrentRequestUri.PathAndQuery,
						UriKind.Relative),
					RedirectOptions.AbsoluteHttps);
		}

		public void SwitchToClearText() {
			Redirect(new Uri(Application.CurrentRequestUri.PathAndQuery,
					UriKind.Relative),
				RedirectOptions.AbsoluteHttp);
		}

		public void EnsureHTTPS() {
			if (SslIsEnabledInConfig()) {
				if (!Application.IsCurrentRequestUsingSsl)
					SwitchToSsl();
			}
		}

		public void EnsureHTTP() {
			if (Application.IsCurrentRequestUsingSsl)
				SwitchToClearText();
		}

		public static bool SslIsEnabledInConfig() {
			// SSL is enabled by default,
			// unless you add an AppSetting for EnableSSL
			// and set it to "false"
			if (String.IsNullOrEmpty(ConfigurationManager.AppSettings["EnableSSL"]))
				return true;
			return Boolean.Parse(ConfigurationManager.AppSettings["EnableSSL"]);
		}

		public static string StripQueryStringFromRelativeUrl(
			Uri possibleAppRelativeUri, out string queryString) {
			// Uri class isn't very friendly to relative URIs
			// have to work with strings here as far as I can tell
			// so I supplied this helper to make it easier
			// to implement IApplication.ResolvePossibleAppRelativeUri
			string appRelativeUrl = possibleAppRelativeUri.ToString();
			int queryIndex = appRelativeUrl.IndexOf('?');
			if (-1 == queryIndex) {
				queryString = "";
				return appRelativeUrl;
			}
			else {
				queryString = appRelativeUrl.Substring(queryIndex + 1);
				return appRelativeUrl.Substring(0, queryIndex);
			}
		}
	}

	public enum RedirectOptions
	{
		Relative,
		AbsoluteHttp,
		AbsoluteHttps,
	}

	public enum ProtocolOptions
	{
		Http,
		Https
	}
}

