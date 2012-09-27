using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Caching;
using System.Web.Mvc;
using Nustache.Core;

namespace Nustache.Mvc
{
    public class NustacheView : IView
    {
        private readonly NustacheViewEngine _engine;
        private readonly ControllerContext _controllerContext;
        private readonly string _viewPath;
        private readonly string _masterPath;

        public NustacheView(
            NustacheViewEngine engine,
            ControllerContext controllerContext,
            string viewPath,
            string masterPath)
        {
            _engine = engine;
            _controllerContext = controllerContext;
            _viewPath = viewPath;
            _masterPath = masterPath;
        }

        public void Render(ViewContext viewContext, TextWriter writer)
        {
            var viewTemplate = GetTemplate();

            var data = _engine.RootContext == NustacheViewEngineRootContext.ViewData
                           ? viewContext.ViewData
                           : viewContext.ViewData.Model;

            if (!string.IsNullOrEmpty(_masterPath))
            {
                var masterTemplate = LoadTemplate(_masterPath);

                masterTemplate.Render(
                    data,
                    writer,
                    name =>
                        {
                            if (name == "Body")
                            {
                                return GetTemplate();
                            }

                            var template = viewTemplate.GetTemplateDefinition(name);

                            if (template != null)
                            {
                                return template;
                            }

                            return FindPartial(name);
                        });
            }
            else
            {
                GetTemplate().Render(data, writer, FindPartial);
            }
        }

        private Template GetTemplate()
        {
            return LoadTemplate(_viewPath);
        }

        private Template LoadTemplate(string path)
        {
            var key = "Nustache:" + path;

            if (_controllerContext.HttpContext.Cache[key] != null)
            {
                return (Template)_controllerContext.HttpContext.Cache[key];
            }

            var templatePath = _controllerContext.HttpContext.Server.MapPath(path);
            var templateSource = File.ReadAllText(templatePath);

            var sb = new StringBuilder();
            MinifyHtml(
                templateSource,
                sb,

                /* Safer to say false, per MinifyHtmlCodeGenerator.cs:28. */
                false
            );
            templateSource = sb.ToString();

            var template = new Template();
            template.Load(new StringReader(templateSource));

            _controllerContext.HttpContext.Cache.Insert(key, template, new CacheDependency(templatePath));

            return template;
        }

        public static readonly char[] _lineSeparators = new [] { '\n', '\r' };

        // Mostly copied from Meleze.Web.Razor.MinifyHtmlMinifier
        private static void MinifyHtml(string content, StringBuilder builder, bool previousIsWhiteSpace) {
            builder.Clear();
            var lines = content.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();
                if (trimmedLine.Length == 0)
                {
                    continue;
                }
                if (!previousIsWhiteSpace && char.IsWhiteSpace(line[0]) && (trimmedLine[0] != '<'))
                {
                    builder.Append(' ');
                }
                builder.Append(Regex.Replace(trimmedLine,@"\s+"," "));

                var endsWithWhiteSpace = char.IsWhiteSpace(line[line.Length - 1]) && (trimmedLine[trimmedLine.Length - 1] != '>');
                var hasEndOfLine = (i < lines.Length - 1) || (_lineSeparators.Any(s => s == content[content.Length - 1]));
                if (hasEndOfLine)
                {
                    builder.Append(' '); // this is a change to the original meleze.web logic which has '\n'
                }
                else if (endsWithWhiteSpace)
                {
                    builder.Append(' ');
                }
                previousIsWhiteSpace = hasEndOfLine || endsWithWhiteSpace;
            }

            content = builder.ToString();
        }

        private Template FindPartial(string name)
        {
            var viewResult = _engine.FindPartialView(_controllerContext, name, false);

            if (viewResult != null)
            {
                if (viewResult.View == null)
                {
                    var stringBuilder = new StringBuilder();

                    foreach (var str in viewResult.SearchedLocations)
                    {
                        stringBuilder.AppendLine();
                        stringBuilder.Append(str);
                    }

                    var msg = string.Format(
                        "The partial view '{0}' was not found or no view engine supports the searched locations. The following locations were searched:{1}",
                        name,
                        stringBuilder);

                    throw new InvalidOperationException(msg);
                }

                var nustacheView = viewResult.View as NustacheView;

                if (nustacheView != null)
                {
                    return nustacheView.GetTemplate();
                }
            }

            return null;
        }
    }
}