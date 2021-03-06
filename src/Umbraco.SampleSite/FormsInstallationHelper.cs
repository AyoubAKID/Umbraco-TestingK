using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Umbraco.Core.Logging;
using Umbraco.Core.Services;

namespace Umbraco.SampleSite
{
    public class FormsInstallationHelper
    {
        private readonly ServiceContext _services;

        private static readonly Regex PreInstallContactFormHtmlPattern = new Regex(@"@Umbraco\.RenderMacro\(\""renderUmbracoForm\""\,[\.\w\{\}\=\(\)\s]+\)", RegexOptions.Compiled);
        private static string PreInstallContactFormHtml = "@Umbraco.RenderMacro(\"renderUmbracoForm\", new { FormGuid = Model.Content.ContactForm.ToString() })";

        private static readonly Regex PostInstallContactFormHtmlPattern = new Regex(@"\<p class=\""compat-msg\""\>.+?\<\/p\>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static string PostInstallContactFormHtml = @"<p class=""compat-msg"">
        <em>Umbraco Forms</em> is required to render this form.It's a breeze to install, all you have to do is
        go to the<em> Umbraco Forms</em> section in the back office and click Install, that's it! :) 
        <br /><br />
        <a href=""/umbraco/#/forms"" class=""button button--border--solid"">Go to Back Office and install Forms</a>
        <!-- When Umbraco Forms is installed, uncomment this line -->
        @* @Umbraco.RenderMacro(""renderUmbracoForm"", new {FormGuid=Model.Content.ContactForm.ToString()}) *@
        </p>";

        public FormsInstallationHelper(ServiceContext services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            _services = services;
        }

        /// <summary>
        /// This will check if Forms is installed and if not it will update the markup in the contact template to render a message to install forms
        /// and update the contactForm property type to become a label
        /// </summary>        
        public void UpdateUmbracoDataForNonFormsInstallation()
        {
            var macroService = _services.MacroService;
            var doctypeService = _services.ContentTypeService;
            var dataTypeService = _services.DataTypeService;
            var fileService = _services.FileService;

            // check if forms is installed
            var formMacro = macroService.GetByAlias("renderUmbracoForm");
            if (formMacro == null)
            {
                // find the doctype and change the form chooser property type

                var contactFormType = doctypeService.GetContentType("contact");
                if (contactFormType != null)
                {
                    var formPicker = contactFormType.PropertyTypes.FirstOrDefault(x => x.Alias == "contactForm");
                    var labelDataType = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias("Umbraco.NoEdit")
                        .First();
                    if (labelDataType != null && formPicker != null)
                    {
                        formPicker.DataTypeDefinitionId = labelDataType.Id;
                        doctypeService.Save(contactFormType);
                    }
                }

                // update the template
                var contactView = fileService.GetTemplate("contact");
                if (contactView != null)
                {
                    var templateContent = contactView.Content;
                    if (string.IsNullOrWhiteSpace(templateContent) == false)
                    {
                        //do the replacement
                        templateContent =
                            PreInstallContactFormHtmlPattern.Replace(templateContent, PostInstallContactFormHtml);

                        contactView.Content = templateContent;
                        fileService.SaveTemplate(contactView);
                    }
                }

            }
            else
            {
                // form is installed
                CreateStarterKitForm();
            }
        }

        /// <summary>
        /// This will check if Forms is installed and if so it will update the markup in the contact template to render the form
        /// and update the contactForm property type to become a form picker
        /// </summary>        
        public void UpdateUmbracoDataForFormsInstallation()
        {
            var macroService = _services.MacroService;
            var doctypeService = _services.ContentTypeService;
            var dataTypeService = _services.DataTypeService;
            var fileService = _services.FileService;

            // check if forms is installed
            var formMacro = macroService.GetByAlias("renderUmbracoForm");
            if (formMacro != null)
            {
                // find the doctype and change the form chooser property type

                var contactFormType = doctypeService.GetContentType("contact");
                if (contactFormType != null)
                {
                    var formPicker = contactFormType.PropertyTypes.FirstOrDefault(x => x.Alias == "contactForm");
                    var formPickerDataType = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias("UmbracoForms.FormPicker").First();
                    if (formPickerDataType != null && formPicker != null)
                    {
                        formPicker.DataTypeDefinitionId = formPickerDataType.Id;
                        doctypeService.Save(contactFormType);
                    }
                }

                // update the template
                var contactView = fileService.GetTemplate("contact");
                if (contactView != null)
                {
                    var templateContent = contactView.Content;
                    if (string.IsNullOrWhiteSpace(templateContent) == false)
                    {
                        //do the replacement
                        templateContent = PostInstallContactFormHtmlPattern.Replace(templateContent, PreInstallContactFormHtml);

                        contactView.Content = templateContent;
                        fileService.SaveTemplate(contactView);
                    }
                }

                CreateStarterKitForm();
            }
        }

        /// <summary>
        /// Will delete the form that is installed by the starter kit if it exists using reflection to invoke the forms API to do so
        /// </summary>
        /// <remarks>
        /// This will not throw any exceptions if the forms types are not found, it will just exit
        /// </remarks>
        public static void RemoveStarterKitForm()
        {
            LogHelper.Info<FormsInstallationHelper>("Deleting Form created from Starter Kit...");

            var formsAssembly = Assembly.Load("Umbraco.Forms.Core");
            if (formsAssembly == null) return;
            var formsType = formsAssembly.GetType("Umbraco.Forms.Core.Form");
            if (formsType == null) return;
            var formsStorageType = formsAssembly.GetType("Umbraco.Forms.Data.Storage.FormStorage");
            if (formsStorageType == null) return;

            //this is the form id that is installed
            var formId = new Guid("adf160f1-39f5-44c0-b01d-9e2da32bf093");

            //create a FormsStorage instance
            object formsStorageInstance;
            try
            {
                formsStorageInstance = Activator.CreateInstance(formsStorageType);
            }
            catch (Exception)
            {
                //If we cannot create it then there's nothing we can do
                return;
            }

            try
            {
                var form = CallMethod(formsStorageInstance, "GetForm", formId);
                if (form == null) return;

                var deleteResult = CallMethod(formsStorageInstance, "DeleteForm", form);
            }
            finally
            {
                CallMethod(formsStorageInstance, "Dispose");
            }

            LogHelper.Info<FormsInstallationHelper>("Deleted Form created from Starter Kit");
        }

        /// <summary>
        /// Creates a Form by importing it via a serialized version of the form and using reflection to invoke the forms API
        /// </summary>
        private static void CreateStarterKitForm()
        {
            LogHelper.Info<FormsInstallationHelper>("Creating Form for Starter Kit...");

            var formsAssembly = Assembly.Load("Umbraco.Forms.Core");
            if (formsAssembly == null)
                throw new InvalidOperationException("Could not load assembly Umbraco.Forms.Core");
            var formsType = formsAssembly.GetType("Umbraco.Forms.Core.Form");
            if (formsType == null) 
                throw new InvalidOperationException("Could not find type Umbraco.Forms.Core.Form in assembly " + formsAssembly);

            //deserialize the form from the json file
            var form = JsonConvert.DeserializeObject(FormsDefinitions.ContactForm, formsType);

            var formsStorageType = formsAssembly.GetType("Umbraco.Forms.Data.Storage.FormStorage");
            if (formsStorageType == null)
                throw new InvalidOperationException("Could not find type Umbraco.Forms.Data.Storage.FormStorage in assembly " + formsAssembly);

            //create a FormsStorage instance
            object formsStorageInstance;
            try
            {
                formsStorageInstance = Activator.CreateInstance(formsStorageType);
            }
            catch (Exception)
            {
                //If we cannot create it then there's nothing we can do
                return;
            }

            //Insert the form, then dispose the FormsStorage
            try
            {
                CallMethod(formsStorageInstance, "InsertForm", form, string.Empty, false);
            }
            finally
            {
                CallMethod(formsStorageInstance, "Dispose");
            }

            LogHelper.Info<FormsInstallationHelper>("Created Form for Starter Kit");
        }

        #region Reflection Helpers

        private static object CallMethod(object obj, string methodName, params object[] parameters)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            var type = obj.GetType();
            var methodInfo = GetMethodInfo(type, methodName, parameters);
            if (methodInfo == null)
                throw new ArgumentOutOfRangeException(nameof(methodName), $"Couldn't find method {methodName} in type {type.FullName}");
            return methodInfo.Invoke(obj, parameters);
        }

        private static MethodInfo GetMethodInfo(Type type, string methodName, object[] parameters)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(methodName));

            var method = type.GetMethod(methodName, 
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, 
                parameters.Select(x => x.GetType()).ToArray(), 
                null);            
            return method;
        }
        
        #endregion
    }
}