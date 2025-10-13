// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.PageGenerators
{
    internal sealed class ForSingleEntityEditForAdmin
    {
        private const string Template = """

                                        <html><body>
                                                <style>
                                                    textarea {
                                                        width: 100%
                                                    }
                                                    input[type=text] {
                                                        width: 100%
                                                    }
                                                    .media_source_base64_drop_area {
                                                        border: 2px dashed #ccc;
                                                        padding: 20px;
                                                        width: 300px;
                                                        text-align: center;
                                                    }
                                                    .media_source_base64_file_input {
                                                        display: none;
                                                    }
                                                    .media_source_base64_preview {
                                                        margin-top: 20px;
                                                    }
                                                </style>
                                        </body></html>
                                        """;
        private readonly string _sanityCheckUrl = RfEndpointMapper.PublicSanityCheckEndpoint;
        private readonly string _lockControlUrl = RfEndpointMapper.PublicEntityLockControlEndpoint;
        private readonly string _crudUrl = RfEndpointMapper.PublicCrudEndpoint;

        private const int MediaSourceMaxFileSizeMb = 8;

        public ForSingleEntityEditForAdmin(
            string entityType,
            int entityId,
            bool requesterUserSuperAdmin = false,
            int requesterUserId = -1)
        {
            _entityName = entityType;

            var defaultObject = RfConfiguration.EntityNameToConfiguration[_entityName].DefaultJObject;
            _supportsTitle = defaultObject.ContainsKey(EntityModelAttributes.Title);
            _supportsParent = defaultObject.ContainsKey(EntityModelAttributes.Parent);
            _supportsFields = defaultObject.ContainsKey(EntityModelAttributes.Fields);

            _entityId = entityId;

            _isThisAddNewOperation = _entityId == -1;
            _isThisCloneFromOperation = _entityId < -1;
            if (_isThisCloneFromOperation)
            {
                _cloneFromId = _entityId * -1;
                _entityId = -1;
            }

            _requesterUserSuperAdmin = requesterUserSuperAdmin;
            _requesterUserId = requesterUserId;

            _operationState = EntityOperationState.CreateStateForGeneralPurposes();
        }
        private IHtmlDocument? _fullDocument;
        private IHtmlElement? _containerElement;
        private CreateElement? _x;

        private readonly string _entityName;
        private readonly int _entityId;
        private readonly bool _requesterUserSuperAdmin;
        private readonly int _requesterUserId;
        private readonly bool _isThisAddNewOperation;
        private readonly bool _isThisCloneFromOperation;
        private readonly int _cloneFromId = -1;
        private readonly EntityOperationState _operationState;

        private readonly bool _supportsTitle;
        private readonly bool _supportsParent;
        private readonly bool _supportsFields;

        private string _jsLogicSaveItem = "";

        public async Task<OperationResult<string>> GenerateAsync(CancellationToken cancellationToken)
        {
            var configuration = RfConfiguration.EntityNameToConfiguration[_entityName];

            switch (configuration.EntityConfiguration.ShallSupportFrontendEdit)
            {
                case SupportsFrontendEdit.No:
                    return OperationResult<string>.Failure($"Entity type {_entityName} is not a candidate to be viewed with an admin frontend.", HttpStatusCode.NotImplemented);
                case SupportsFrontendEdit.ForSuperAdminOnly
                    when !_requesterUserSuperAdmin:
                    return OperationResult<string>.Failure($"Forbidden", HttpStatusCode.Forbidden);
                case SupportsFrontendEdit.ForAllAuthorized:
                default:
                    break;
            }

            JObject? entityObj;
            if (_isThisAddNewOperation || _isThisCloneFromOperation) //Add new or Clone from
            {
                entityObj = (JObject)configuration.DefaultJObject.DeepClone();

                if (_supportsFields)
                {
                    if (_isThisAddNewOperation)
                    {
                        entityObj[EntityModelAttributes.Fields] =
                            await EntityModelDefaultsBuilder.CreateDefaultEntityFieldsObjectAsync(_entityName, _operationState, true, cancellationToken);
                    }
                    else //Clone
                    {
                        var getResult =
                            await _operationState.GetEntityInOperationAsync(_entityName, _cloneFromId, cancellationToken);
                        if (!getResult.IsSuccessful)
                        {
                            return OperationResult<string>.Failure(getResult.ErrorMessage, getResult.StatusCode);
                        }

                        var copyFromFullObject = (JObject)getResult.Data.DeepClone();

                        if (!copyFromFullObject.TryGetTypedValue(EntityModelAttributes.Fields, out JObject? fetchedFields))
                        {
                            return OperationResult<string>.Failure($"Failed to clone from {_cloneFromId}. Entity does not have -{EntityModelAttributes.Fields}- field.", HttpStatusCode.InternalServerError);
                        }
                        if (fetchedFields != null) entityObj[EntityModelAttributes.Fields] = (JObject)fetchedFields.DeepClone();
                    }
                }

                entityObj[EntityModelAttributes.Id] = -1;
                if (_requesterUserId >= 1 && entityObj.ContainsKey(EntityModelAttributes.Author))
                {
                    entityObj[EntityModelAttributes.Author] = _requesterUserId;
                }

                _jsLogicSaveItem = $$"""

                                     let create_request = new XMLHttpRequest();
                                     create_request.withCredentials = true;
                                     create_request.open('POST', '{{RfEndpointMapper.PublicCrudEndpoint}}?operation=CREATE&type={{_entityName}}');
                                     create_request.setRequestHeader('Content-Type', 'application/json');
                                     create_request.onreadystatechange = function() {
                                         if (this.readyState === XMLHttpRequest.DONE) {
                                             let parsed;
                                             try { parsed = JSON.parse(this.responseText); } catch (e) { parsed = { message: 'Create request has failed.' } }

                                             if (this.status === 200) {
                                                 if (!('{{EntityModelAttributes.Id}}' in parsed)) {
                                                     window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{_entityName}}';
                                                     return;
                                                 }
                                                 window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{_entityName}}&{{EntityModelAttributes.Id}}=' + parsed.{{EntityModelAttributes.Id}};
                                             }
                                             else {
                                                 if (!('message' in parsed)) parsed.message = 'Create request has failed.';
                                                 iziToast.error({
                                                     message: parsed.message,
                                                     timeout: 10000
                                                 });
                                             }
                                         }
                                     };
                                     create_request.send(JSON.stringify(window.internal_full_state));
                                     """;
            }
            else
            {
                var getResult = await _operationState.GetEntityInOperationAsync(_entityName, _entityId, cancellationToken);

                if (!getResult.IsSuccessful)
                {
                    return OperationResult<string>.Failure(getResult.ErrorMessage, getResult.StatusCode);
                }
                entityObj = (JObject)getResult.Data.DeepClone();

                _jsLogicSaveItem = $$"""

                                     let update_request = new XMLHttpRequest();
                                     update_request.withCredentials = true;
                                     update_request.open('POST', '{{RfEndpointMapper.PublicCrudEndpoint}}?operation=UPDATE&type={{_entityName}}');
                                     update_request.setRequestHeader('Content-Type', 'application/json');
                                     update_request.onreadystatechange = function() {
                                         if (this.readyState === XMLHttpRequest.DONE) {
                                             if (this.status === 200) {
                                                 iziToast.success({
                                                     message: 'Changes have successfully been saved.',
                                                     progressBar: false
                                                 });
                                             }
                                             else {
                                                 let parsed;
                                                 try { parsed = JSON.parse(this.responseText); } catch (e) { parsed = { message: 'Update request has failed.' } }
                                                 if (!('message' in parsed)) parsed.message = 'Update request has failed.';
                                                 iziToast.error({
                                                     message: parsed.message,
                                                     timeout: 10000
                                                 });
                                             }
                                         }
                                     };
                                     update_request.send(JSON.stringify(window.internal_full_state));
                                     """;
            }

            _fullDocument = await new HtmlParser().ParseDocumentAsync(Template, cancellationToken);
            _x = _fullDocument.AsCreateElement();
            _containerElement = _fullDocument.CreateElement<IHtmlDivElement>();
            _fullDocument.Body?.AppendChild(_containerElement);

            var compileEntityResult = await CompileEntityAsync(configuration.EntityModelType, entityObj, cancellationToken);
            if (!compileEntityResult.IsSuccessful)
            {
                return OperationResult<string>.Failure($"Compilation failed for {_entityName} for {EntityModelAttributes.Id} {_entityId}. Error: {compileEntityResult.ErrorMessage}.", HttpStatusCode.InternalServerError);
            }

            var error = "";
            return HtmlUtility.ConvertHtmlDocumentToHtmlString(
                _fullDocument,
                out var compiled,
                err => error += err + Environment.NewLine)
                ? OperationResult<string>.Success(compiled.NotNull())
                : OperationResult<string>.Failure($"ConvertHTMLDocumentToHTMLString has failed for {_entityName} for {EntityModelAttributes.Id} {_entityId}. Error: {error}", HttpStatusCode.InternalServerError);
        }

        private async Task<OperationResult<bool>> CompileEntityAsync(Type fieldsModelTypeNullable, JObject entityObject, CancellationToken cancellationToken)
        {
            if (_fullDocument == null || _containerElement == null) return OperationResult<bool>.Failure($"Internal error. Document/ContainerElement is null.", HttpStatusCode.InternalServerError);

            JObject? entityTitleObjectNullable = null;
            if (_supportsTitle && !entityObject.TryGetTypedValue(EntityModelAttributes.Title, out entityTitleObjectNullable))
            {
                return OperationResult<bool>.Failure($"Failed to compile entity for {_entityName} for {EntityModelAttributes.Id} {_entityId}. Entity does not have -{EntityModelAttributes.Title}- field.", HttpStatusCode.BadRequest);
            }

            JObject? entityFieldsObject = null;
            if (_supportsFields && !entityObject.TryGetTypedValue(EntityModelAttributes.Fields, out entityFieldsObject))
            {
                return OperationResult<bool>.Failure($"Failed to compile entity for {_entityName} for {EntityModelAttributes.Id} {_entityId}. Entity does not have -{EntityModelAttributes.Fields}- field.", HttpStatusCode.BadRequest);
            }

            var jsFunctionAtFirst = (IHtmlScriptElement?)_fullDocument?.CreateElement("script");
            if (jsFunctionAtFirst == null) return OperationResult<bool>.Failure($"Internal error. Failed to create script element.", HttpStatusCode.InternalServerError);

            _fullDocument?.Body?.AppendChild(jsFunctionAtFirst);
            jsFunctionAtFirst.Type = "text/javascript";
            jsFunctionAtFirst.InnerHtml = $$"""

                                            window.parent_supported = '__[[PARENT_SUPPORTED]]__';
                                            window.title_supported = '__[[TITLE_SUPPORTED]]__';
                                            window.tags_supported = '__[[TAGS_SUPPORTED]]__';
                                            window.categories_supported = '__[[CATEGORIES_SUPPORTED]]__';
                                            window.fields_supported = '__[[FIELDS_SUPPORTED]]__';

                                            window.internal_full_state = '__[[INTERNAL_FULL_STATE]]__';

                                            window.internal_parent_state = '__[[INTERNAL_PARENT_STATE]]__';
                                            window.internal_title_state = '__[[INTERNAL_TITLE_STATE]]__';
                                            window.internal_tags_state = '__[[INTERNAL_TAGS_STATE]]__';
                                            window.internal_categories_state = '__[[INTERNAL_CATEGORIES_STATE]]__';
                                            window.internal_current_fields_state = '__[[INTERNAL_FIELDS_STATE]]__';

                                            if (window.fields_supported) {
                                                window.deleted = '__DELETED__';
                                                window.remove_object_from_deleted_items = function(obj) {
                                                    if (Array.isArray(obj)) {
                                                        for (let i = obj.length - 1; i >= 0; i--) {
                                                            if (obj[i] === window.deleted) {
                                                                obj.splice(i, 1);
                                                            }
                                                            else {
                                                                window.remove_object_from_deleted_items(obj[i]);
                                                            }
                                                        }
                                                    }
                                                    else if (typeof obj === 'object' && obj !== null) {
                                                        let to_be_removed = [];
                                                        for (let key in obj) {
                                                            if (obj[key] === window.deleted) {
                                                                to_be_removed.push(key);
                                                            }
                                                            else {
                                                                window.remove_object_from_deleted_items(obj[key]);
                                                            }
                                                        }
                                                        for (let i = 0; i < to_be_removed.length; i++) {
                                                            delete obj[to_be_removed[i]];
                                                        }
                                                    }
                                                };
                                                window.has_parent_with_reserved_class = function(element) {
                                                    while (element.parentElement) {
                                                        if (element.parentElement.classList.contains('fields_reserve_element')) return true;
                                                        element = element.parentElement;
                                                    }
                                                    return false;
                                                };
                                                window.evaluate_conditions_on_fields_state_change = function() {
                                                    let elements_with_display_condition = document.querySelectorAll('[data-display-condition]');
                                                    for (let i = 0; i < elements_with_display_condition.length; i++) {
                                                        let current_element = elements_with_display_condition[i];
                                                        if (window.has_parent_with_reserved_class(current_element)) continue;

                                                        let tb_eval = "window.current_fields_state." + current_element.getAttribute('data-display-condition');
                                                        if (eval(tb_eval) === false) {
                                                            current_element.add_bs_class('d-none');
                                                        }
                                                        else {
                                                            current_element.remove_bs_class('d-none');
                                                        }
                                                    }
                                                };
                                                window.evaluate_select_options_on_fields_state_change = function() {
                                                    Array.from(document.querySelectorAll('[dynamic-options-function]')).forEach(select_el => {
                                                        select_el.innerHTML = '';
                                                        let tb_eval = "window.latest_dynamic_options_input = window.current_fields_state." + select_el.getAttribute('dynamic-options-input-path') + '; ' + select_el.getAttribute('dynamic-options-function');
                                                        try {
                                                            window.eval_async_code(tb_eval).then(new_options => {
                                                                for (let i = 0; i < new_options.length; i++) {
                                                                    const splitted = new_options[i].split(' : ');
                                                                    const new_option = select_el.appendChild(document.createElement('option'));
                                                                    new_option.value = splitted[0];
                                                                    new_option.text = splitted[1];
                                                                }
                                                            });
                                                        }
                                                        catch (e) {
                                                            select_el.innerHTML = '';
                                                            console.log(tb_eval);
                                                        }
                                                        finally {
                                                            //window.latest_dynamic_options_input = null;
                                                        }
                                                    });
                                                };
                                                window.eval_async_code = async (js_code) => {
                                                    return await eval(`(async () => { ${js_code} })()`);
                                                };
                                                window.media_source_base64_handle_file = function(file, preview_element, on_file_ready) {
                                                    if (!file.type.startsWith('image/')) {
                                                        iziToast.error({
                                                            message: 'Please select an image file.',
                                                            timeout: 10000
                                                        });
                                                        return;
                                                    }

                                                    if (file.size > {{MediaSourceMaxFileSizeMb}} * 1024 * 1024) {
                                                        iziToast.error({
                                                            message: 'File size exceeds the maximum limit of {{MediaSourceMaxFileSizeMb}}MB.',
                                                            timeout: 10000
                                                        });
                                                        return;
                                                    }

                                                    const fr = new FileReader();
                                                    fr.onloadend = () => {
                                                        const base_64_str = fr.result;

                                                        preview_element.src = base_64_str;
                                                        preview_element.remove_bs_class('d-none');

                                                        on_file_ready(base_64_str);
                                                    };
                                                    fr.readAsDataURL(file);
                                                };
                                            }

                                            window.inactivity_timer = null;
                                            window.cancel_inactivity_timer = function() {
                                                if (window.inactivity_timer !== null) {
                                                    clearTimeout(window.inactivity_timer);
                                                    window.inactivity_timer = null;
                                                }
                                            };
                                            window.reset_inactivity_timer = function() {
                                                window.cancel_inactivity_timer();
                                                window.inactivity_timer = setTimeout(function() {
                                                    Swal.fire({
                                                        title: 'Inactivity',
                                                        html: 'You have been inactive for more than 10 minutes.<br>'
                                                            + 'You will be redirected to view-only mode.<br><br>'
                                                            + 'Auto-redirection in <strong>10</strong> seconds.<br><br>'
                                                            + '<button id="close_immediately" class="btn btn-success">Go to view-only mode</button>',
                                                        timer: 10000,
                                                        didOpen: () => {
                                                            Swal.getHtmlContainer().querySelector('#close_immediately').addEventListener('click', function() {
                                                                try { Swal.close(); } catch (e) {}
                                                            });
                                                            setInterval(() => {
                                                              Swal.getHtmlContainer().querySelector('strong')
                                                                .textContent = (Swal.getTimerLeft() / 1000)
                                                                  .toFixed(0)
                                                            }, 100);
                                                        },
                                                        willClose: () => {
                                                            window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesBaseRoute}}?type={{_entityName}}&id={{_entityId}}';
                                                        },
                                                        showCloseButton: false, showCancelButton: false, showDenyButton: false, showConfirmButton: false, allowEnterKey: false, allowEscapeKey: false, allowOutsideClick: false
                                                    });
                                                }, 600000);
                                            };

                                            window.global_oninput = function(self_ptr) {
                                                window.reset_inactivity_timer();

                                                if (window.save_changes_delay_id !== null) {
                                                    clearTimeout(window.save_changes_delay_id);
                                                    window.save_changes_delay_id = null;
                                                }

                                                iziToast.destroy();
                                                if (self_ptr.hasAttribute('data-oninput-timer-id')) {
                                                    clearTimeout(parseInt(self_ptr.getAttribute('data-oninput-timer-id')));
                                                    self_ptr.removeAttribute('data-oninput-timer-id');
                                                }
                                                self_ptr.setAttribute('data-oninput-timer-id', setTimeout(function() {
                                                    if (self_ptr) {
                                                        self_ptr.removeAttribute('data-oninput-timer-id');
                                                        self_ptr.dispatchEvent(new Event('change'));
                                                    }
                                                }, 7500).toString());
                                            };

                                            window.save_changes_delay_id = null;
                                            window.sanity_check_in_progress = null;
                                            window.refresh_relation_list_in_progress = null;
                                            window.on_states_changed = function() {
                                                window.reset_inactivity_timer();

                                                let elements_with_oninput_timers = document.querySelectorAll('[data-oninput-timer-id]');
                                                for (let i = 0; i < elements_with_oninput_timers.length; i++) {
                                                    let current_element = elements_with_oninput_timers[i];
                                                    clearTimeout(parseInt(current_element.getAttribute('data-oninput-timer-id')));
                                                    current_element.removeAttribute('data-oninput-timer-id');
                                                }

                                                if (window.title_supported) {
                                                    let title_deep_cloned = JSON.parse(JSON.stringify(window.current_title_state));
                                                    window.internal_full_state.{{EntityModelAttributes.Title}} = title_deep_cloned;
                                                }

                                                if (window.parent_supported) {
                                                    let parent_id = window.current_parent_state.id;
                                                    window.internal_full_state.{{EntityModelAttributes.Parent}} = parent_id;
                                                }

                                                if (window.fields_supported) {
                                                    let fields_deep_cloned = JSON.parse(JSON.stringify(window.current_fields_state));
                                                    window.remove_object_from_deleted_items(fields_deep_cloned);
                                                    window.internal_full_state.{{EntityModelAttributes.Fields}} = fields_deep_cloned;
                                                }

                                                if (window.tags_supported) {
                                                    let tags_cloned = JSON.parse(JSON.stringify(window.current_tags_state.array));
                                                    window.internal_full_state.{{EntityModelAttributes.Tags}} = tags_cloned;
                                                }

                                                if (window.categories_supported) {
                                                    let categories_cloned = JSON.parse(JSON.stringify(window.current_categories_state.array));
                                                    window.internal_full_state.{{EntityModelAttributes.Categories}} = categories_cloned;
                                                }

                                                let request_object = new XMLHttpRequest();

                                                window.sanity_check_in_progress = window.generate_random_string(8);
                                                request_object.sanity_check_id = window.sanity_check_in_progress;

                                                request_object.withCredentials = true;
                                                request_object.open('POST', '{{_sanityCheckUrl}}?type={{_entityName}}');
                                                request_object.setRequestHeader('Content-Type', 'application/json');
                                                request_object.onreadystatechange = function() {
                                                    if (this.sanity_check_id !== window.sanity_check_in_progress) return;

                                                    if (this.readyState === XMLHttpRequest.DONE) {
                                                        let parsed;
                                                        try { parsed = JSON.parse(this.responseText); } catch (e) { parsed = { message: 'Failed to parse the response.' } }
                                                        if (!('message' in parsed)) parsed.message = 'Response does not contain any message.';

                                                        if (this.status >= 400) {
                                                            iziToast.destroy();
                                                            iziToast.error({
                                                                message: parsed.message,
                                                                timeout: 10000
                                                            });
                                                            console.error(window.internal_full_state);
                                                        }
                                                        else {
                                                            if (window.save_changes_delay_id !== null) {
                                                                clearTimeout(window.save_changes_delay_id);
                                                            }
                                                            iziToast.destroy();
                                                            iziToast.info({
                                                                message: 'Your changes will be saved...',
                                                                timeout: 5000
                                                            });

                                                            window.save_changes_delay_id = setTimeout(function() {
                                                                window.save_changes_delay_id = null;

                                                                {{_jsLogicSaveItem}}
                                                            }, 5000);
                                                        }
                                                    }
                                                };
                                                request_object.send(JSON.stringify(window.internal_full_state));

                                                if (window.fields_supported) {
                                                    window.evaluate_conditions_on_fields_state_change();
                                                    window.evaluate_select_options_on_fields_state_change();
                                                }

                                                return true;
                                            };

                                            if (window.title_supported) {
                                                window.current_title_state = ObservableSlim.create(window.internal_title_state, true, function(changes) {
                                                    return window.on_states_changed();
                                                });
                                            }

                                            if (window.parent_supported) {
                                                window.current_parent_state = ObservableSlim.create(window.internal_parent_state, true, function(changes) {
                                                    return window.on_states_changed();
                                                });
                                            }

                                            if (window.tags_supported) {
                                                window.current_tags_state = ObservableSlim.create(window.internal_tags_state, true, function(changes) {
                                                    return window.on_states_changed();
                                                });
                                            }

                                            if (window.categories_supported) {
                                                window.current_categories_state = ObservableSlim.create(window.internal_categories_state, true, function(changes) {
                                                    return window.on_states_changed();
                                                });
                                            }

                                            if (window.fields_supported) {
                                                window.current_fields_state = ObservableSlim.create(window.internal_current_fields_state, true, function(changes) {
                                                    return window.on_states_changed();
                                                });
                                            }

                                            window.generate_random_string = function(generate_length) {
                                                const characters = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
                                                let result = '';
                                                const characters_length = characters.length;

                                                for (let i = 0; i < generate_length; i++) {
                                                    const random_index = Math.floor(Math.random() * characters_length);
                                                    result += characters.charAt(random_index);
                                                }
                                                return result;
                                            };

                                            window.replace_unique_ids_in_jobject = function(obj, mapping) {
                                                if (obj === null) return;

                                                for (const key in obj) {
                                                    if (key === '{{BaseModel.UniqueFieldIdPropertyName}}') {
                                                        if (obj[key] in mapping) {
                                                            obj[key] = mapping[obj[key]];
                                                        }
                                                        else {
                                                            let new_id = window.generate_random_string(16);
                                                            mapping[obj[key]] = new_id;
                                                            obj[key] = new_id;
                                                        }
                                                    }
                                                    else if (obj[key] !== null) {
                                                        if (typeof obj[key] === 'object') {
                                                            window.replace_unique_ids_in_jobject(obj[key], mapping);
                                                        }
                                                        else if (Array.isArray(obj[key])) {
                                                            for (let i = 0; i < obj[key].length; i++) {
                                                                window.replace_unique_ids_in_jobject(obj[key][i], mapping);
                                                            }
                                                        }
                                                    }
                                                }
                                            };

                                            window.change_object_and_element_recursively_with_proper_unique_field_ids = function(new_element_div, deep_cloned_new_element) {
                                                let mapping = {};
                                                window.replace_unique_ids_in_jobject(deep_cloned_new_element, mapping);

                                                const make_script_changes = function(node) {
                                                    if (node === null) return;
                                                    if (node instanceof HTMLScriptElement) {
                                                        let prev_script_content = node.textContent;
                                                        for (const key in mapping) {
                                                            node.textContent = node.textContent.replace(new RegExp(key, 'g'), mapping[key]);
                                                        }
                                                        if (prev_script_content !== node.textContent) {
                                                            eval(node.textContent);
                                                        }
                                                        else {
                                                            window.last_table_element_waiting_to_be_processed_for_setup_buttons = node.parentElement;
                                                            eval(node.textContent);
                                                            window.last_table_element_waiting_to_be_processed_for_setup_buttons = null;
                                                        }
                                                    }
                                                    else {
                                                        if (typeof node.hasAttribute === 'undefined') return;
                                                        if (node.hasAttribute('onchange')) {
                                                            let new_script = node.getAttribute('onchange');
                                                            if (new_script !== undefined && new_script !== null && new_script.length > 0) {
                                                                for (const key in mapping) {
                                                                    new_script = new_script.replace(new RegExp(key, 'g'), mapping[key]);
                                                                }
                                                                node.setAttribute('onchange', new_script);
                                                            }
                                                        }
                                                        if (node.hasAttribute('onclick')) {
                                                            let new_script = node.getAttribute('onclick');
                                                            if (new_script !== undefined && new_script !== null && new_script.length > 0) {
                                                                for (const key in mapping) {
                                                                    new_script = new_script.replace(new RegExp(key, 'g'), mapping[key]);
                                                                }
                                                                node.setAttribute('onclick', new_script);
                                                            }
                                                        }
                                                    }
                                                };
                                                const self_and_all_descendants = function(node) {
                                                    make_script_changes(node);
                                                    for (let i = 0; i < node.childNodes.length; i++) {
                                                        self_and_all_descendants(node.childNodes[i]);
                                                    }
                                                };
                                                self_and_all_descendants(new_element_div);
                                            };

                                            window.refresh_relation_list = function(element, entity_type) {
                                                window.showThrobber();

                                                let request_object = new XMLHttpRequest();

                                                window.refresh_relation_list_in_progress = window.generate_random_string(8);
                                                request_object.refresh_relation_list_id = window.refresh_relation_list_in_progress;

                                                request_object.withCredentials = true;
                                                request_object.open('POST', '{{_crudUrl}}?action=crud&operation=PEEK_ALL&type=' + entity_type);
                                                request_object.setRequestHeader('Content-Type', 'application/json');
                                                request_object.onreadystatechange = function() {
                                                    if (this.refresh_relation_list_id !== window.refresh_relation_list_in_progress) return;

                                                    if (this.readyState === XMLHttpRequest.DONE) {
                                                        window.hideThrobber();

                                                        let parse_success = true;
                                                        let parsed;
                                                        try {
                                                            parsed = JSON.parse(this.responseText);
                                                        } catch (e) {
                                                            parsed = { message: 'Get list of entities request has failed.' };
                                                            parse_success = false;
                                                        }

                                                        if (parse_success && this.status === 200) {
                                                            let map = new Map();
                                                            for (let i = 0; i < parsed.length; i++) {
                                                                let title = parsed[i].title === undefined ? parsed[i].name : parsed[i].title;

                                                                if (map.has(title)) {
                                                                    map.get(title).push(parsed[i].id);
                                                                }
                                                                else {
                                                                    map.set(title, [parsed[i].id]);
                                                                }
                                                            }

                                                            let selected_index = element.selectedIndex;
                                                            let selected_id = -1;
                                                            if (selected_index >= 0) {
                                                                selected_id = Number(element.options[selected_index].value);
                                                            }

                                                            element.innerHTML = '';
                                                            let default_option = document.createElement('option');
                                                            default_option.value = '-1';
                                                            default_option.text = '';
                                                            element.appendChild(default_option);

                                                            let any_selected = false;
                                                            var map_asc = new Map([...map.entries()].sort());
                                                            map_asc.forEach((id_array, title) => {
                                                                for (let i = 0; i < id_array.length; i++) {
                                                                    let new_option = document.createElement('option');
                                                                    new_option.value = id_array[i].toString();
                                                                    new_option.text = title + (id_array.length > 1 ? (' (Id: ' + id_array[i] + ')') : '');
                                                                    element.appendChild(new_option);

                                                                    if (selected_id === id_array[i]) {
                                                                        new_option.selected = true;
                                                                        any_selected = true;
                                                                    }
                                                                }
                                                            });
                                                            if (!any_selected) {
                                                                default_option.selected = true;
                                                            }
                                                        }
                                                        else {
                                                            if (!('message' in parsed)) parsed.message = 'Refresh ' + entity_type + ' list request has failed.';
                                                            iziToast.error({
                                                                message: parsed.message,
                                                                timeout: 10000
                                                            });
                                                        }
                                                    }
                                                };
                                                request_object.send('{}');
                                            };

                                            window.increment_sibling_input_range_by = function(button_element, increment_by, minimum_value, maximum_value) {
                                                let range_element = button_element.parentNode.querySelector('input');
                                                let new_value = Number(range_element.value) + increment_by;
                                                if (new_value > maximum_value || new_value < minimum_value) return;
                                                range_element.value = new_value;
                                                range_element.dispatchEvent(new Event('change'));
                                            };

                                            window.switch_elements = (old_el, new_el) => {
                                                let old_parent = old_el.parentElement;
                                                let new_parent = new_el.parentElement;

                                                let old_sibling = old_el.nextElementSibling;
                                                let new_sibling = new_el.nextElementSibling;

                                                if (old_sibling) {
                                                    old_parent.insertBefore(new_el, old_sibling);
                                                } else {
                                                    old_parent.appendChild(new_el);
                                                }

                                                if (new_sibling) {
                                                    new_parent.insertBefore(old_el, new_sibling);
                                                } else {
                                                    new_parent.appendChild(old_el);
                                                }
                                            };

                                            window.switch_dnone_status_of_elements = (old_el, new_el) => {
                                                if (old_el.classList.contains('d-none')) {
                                                    if (!new_el.classList.contains('d-none')) {
                                                        old_el.remove_bs_class('d-none');
                                                        new_el.add_bs_class('d-none');
                                                    }
                                                }
                                                else {
                                                    if (new_el.classList.contains('d-none')) {
                                                        old_el.add_bs_class('d-none');
                                                        new_el.remove_bs_class('d-none');
                                                    }
                                                }
                                            };

                                            window.switch_attribute_status = (attribute_name, old_el, new_el) => {
                                                if (old_el.hasAttribute(attribute_name)) {
                                                    if (!new_el.hasAttribute(attribute_name)) {
                                                        new_el.setAttribute(attribute_name, old_el.getAttribute(attribute_name));
                                                        old_el.removeAttribute(attribute_name);
                                                    }
                                                    else {
                                                        const tmp = old_el.getAttribute(attribute_name);
                                                        old_el.setAttribute(attribute_name, new_el.getAttribute(attribute_name));
                                                        new_el.setAttribute(attribute_name, tmp);
                                                    }
                                                }
                                                else {
                                                    if (new_el.hasAttribute(attribute_name)) {
                                                        old_el.setAttribute(attribute_name, new_el.getAttribute(attribute_name));
                                                        new_el.removeAttribute(attribute_name);
                                                    }
                                                }
                                            };

                                            window.switch_inner_html_of_elements = (old_el, new_el) => {
                                                const tmp = old_el.innerHTML;
                                                old_el.innerHTML = new_el.innerHTML;
                                                new_el.innerHTML = tmp;
                                            };

                                            window.collapse_show = (element) => {
                                                return new Promise((resolve) => {
                                                    const $element = $(element);

                                                    const on_collapse_shown = () => {
                                                        $element.off('shown.bs.collapse', on_collapse_shown);
                                                        resolve();
                                                    };

                                                    $element.on('shown.bs.collapse', on_collapse_shown);
                                                    $element.collapse('show');
                                                });
                                            };

                                            """;

            if (_supportsTitle)
            {
                GenerateTitleEditField(entityTitleObjectNullable);
            }

            var parentId = -1;
            if (_supportsParent)
            {
                if (!entityObject.TryGetTypedValue(EntityModelAttributes.Parent, out parentId))
                {
                    parentId = -1;
                }

                await GenerateParentEditField(parentId, cancellationToken);
            }

            var configuration = RfConfiguration.EntityNameToConfiguration[_entityName].EntityConfiguration;

            var supportsTags = configuration.HasTags;
            var supportsCategories = configuration.HasCategories;

            if (_requesterUserId > 0 && (supportsTags || supportsCategories))
            {
                var userObject = RfConfiguration.UserEntitiesCache.GetEntityCopy(_requesterUserId);
                if (userObject == null)
                    return OperationResult<bool>.Failure($"Requester user not found.", HttpStatusCode.Unauthorized);
                var userFields = userObject.Fields;

                if (supportsTags
                    && userFields.CanUserDo("UPDATE", RfReservedEntities.TagsEntityName))
                {
                    var canCreateNewTag =
                        userFields.CanUserDo("CREATE", RfReservedEntities.TagsEntityName);

                    if (!entityObject.TryGetTypedValue(EntityModelAttributes.Tags, out List<int> currentTagIds))
                        currentTagIds = [];

                    GenerateTaxonomyEditField(RfReservedEntities.TagsEntityName, currentTagIds, canCreateNewTag);
                }

                if (supportsCategories
                    && userFields.CanUserDo("UPDATE", RfReservedEntities.CategoriesEntityName))
                {
                    var canCreateNewCategory = userFields.CanUserDo("CREATE", RfReservedEntities.CategoriesEntityName);

                    if (!entityObject.TryGetTypedValue(EntityModelAttributes.Categories, out List<int> currentCategoryIds))
                        currentCategoryIds = [];

                    GenerateTaxonomyEditField(RfReservedEntities.CategoriesEntityName, currentCategoryIds, canCreateNewCategory);
                }
            }

            if (_supportsFields)
            {
                await EntityViewBuilder.JObjectGenerateAdminFrontendHtmlAsync(
                    _entityName,
                    _fullDocument.AsCreateElement(),
                    _containerElement,
                    fieldsModelTypeNullable,
                    entityFieldsObject.NotNull(),
                    "",
                    0,
                    GroupRenderStyle.Full,
                    _operationState,
                    false,
                    cancellationToken);
            }

            jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[PARENT_SUPPORTED]]__'",
                _supportsParent ? "true" : "false");
            jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[TITLE_SUPPORTED]]__'",
                _supportsTitle ? "true" : "false");
            jsFunctionAtFirst.InnerHtml =
                jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[TAGS_SUPPORTED]]__'",
                    supportsTags ? "true" : "false");
            jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[CATEGORIES_SUPPORTED]]__'",
                supportsCategories ? "true" : "false");
            jsFunctionAtFirst.InnerHtml =
                jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[FIELDS_SUPPORTED]]__'",
                    _supportsFields ? "true" : "false");

            jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst("'__[[INTERNAL_FULL_STATE]]__'",
                entityObject.ToString(Newtonsoft.Json.Formatting.None));

            if (_supportsTitle)
            {
                jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst(
                    "'__[[INTERNAL_TITLE_STATE]]__'",
                    entityTitleObjectNullable.NotNull().ToString(Newtonsoft.Json.Formatting.None));
            }

            if (_supportsParent)
            {
                jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst(
                    "'__[[INTERNAL_PARENT_STATE]]__'",
                    (new JObject { [EntityModelAttributes.Id] = parentId }).ToString(Newtonsoft.Json.Formatting.None));
            }

            if (supportsTags)
            {
                jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst(
                    "'__[[INTERNAL_TAGS_STATE]]__'",
                    (new JObject
                    {
                        ["array"] = entityObject.TryGetValue(EntityModelAttributes.Tags, out var tagsJArray)
                            ? (JArray)tagsJArray
                            : []
                    }).ToString(Newtonsoft.Json.Formatting.None));
            }

            if (supportsCategories)
            {
                jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst(
                    "'__[[INTERNAL_CATEGORIES_STATE]]__'",
                    (new JObject
                    {
                        ["array"] = entityObject.TryGetValue(EntityModelAttributes.Categories, out var categoriesJArray)
                            ? (JArray)categoriesJArray
                            : []
                    }).ToString(Newtonsoft.Json.Formatting.None));
            }

            if (_supportsFields)
            {
                jsFunctionAtFirst.InnerHtml = jsFunctionAtFirst.InnerHtml.ReplaceFirst(
                    "'__[[INTERNAL_FIELDS_STATE]]__'",
                    entityFieldsObject.NotNull().ToString(Newtonsoft.Json.Formatting.None));
            }

            if (!_isThisAddNewOperation && !_isThisCloneFromOperation)
            {
                jsFunctionAtFirst.InnerHtml =
                    GetLockRelatedJsContent() + Environment.NewLine + jsFunctionAtFirst.InnerHtml;
            }

            var jsFunctionAtLast = _fullDocument.NotNull().CreateElement<IHtmlScriptElement>();
            _fullDocument?.Body?.AppendChild(jsFunctionAtLast);
            jsFunctionAtLast.Type = "text/javascript";
            jsFunctionAtLast.InnerHtml = """

                                         if (window.fields_supported) {
                                             window.evaluate_conditions_on_fields_state_change();
                                             window.evaluate_select_options_on_fields_state_change();
                                         }
                                         """;

            return OperationResult<bool>.Success(true);
        }

        private string GetLockRelatedJsContent()
        {
            return $$"""

                     if (window.entity_lock_control === undefined) {
                         let created_intervals = [];
                         let stop_created_intervals = function() {
                             window.cancel_inactivity_timer();
                             for (let i = 0; i < created_intervals.length; i++) {
                                 clearInterval(created_intervals[i]);
                             }
                             created_intervals = [];
                         };
                         window.entity_lock_control = function() {
                             let lock_control_request_object = new XMLHttpRequest();
                             lock_control_request_object.withCredentials = true;
                             lock_control_request_object.open('POST', '{{_lockControlUrl}}?type={{_entityName}}&id={{_entityId}}&operation=try_lock');
                             lock_control_request_object.setRequestHeader('Content-Type', 'application/json');
                             lock_control_request_object.onreadystatechange = function() {
                                 if (this.readyState === XMLHttpRequest.DONE) {
                                     if (this.status >= 400) {
                                         stop_created_intervals();
                                         let check_lock_owner_request_object = new XMLHttpRequest();
                                         check_lock_owner_request_object.withCredentials = true;
                                         check_lock_owner_request_object.open('GET', '{{_lockControlUrl}}?type={{_entityName}}&id={{_entityId}}&operation=status_one');
                                         check_lock_owner_request_object.onreadystatechange = function() {
                                             if (this.readyState === XMLHttpRequest.DONE) {
                                                 let parsed;
                                                 let failed = this.status >= 400;
                                                 if (!failed) {
                                                     try {
                                                         parsed = JSON.parse(this.responseText);
                                                     }
                                                     catch (e) {
                                                         failed = true;
                                                     }
                                                     if (!failed) {
                                                         if (!('locked_by_user_name' in parsed)) failed = true;
                                                     }
                                                 }
                                                 if (failed) {
                                                     Swal.fire({title: 'Fatal error.',
                                                         html: 'Lock status acquisition request has failed. Please try again.',
                                                         showConfirmButton: true, confirmButtonText: 'Refresh the Page',
                                                         showCloseButton: false, showCancelButton: false, showDenyButton: false, allowEnterKey: false, allowEscapeKey: false, allowOutsideClick: false
                                                     }).then((result) => {
                                                         if (result.isConfirmed) {
                                                             location.reload();
                                                         }
                                                     });
                                                     return;
                                                 }
                                                 Swal.fire({title: 'The entity is being edited by someone else.',
                                                     html: 'Currently ' + parsed.locked_by_user_name + ' is editing the entity.',
                                                     showConfirmButton: true, confirmButtonText: 'Go back',
                                                     showCloseButton: false, showCancelButton: false, showDenyButton: false, allowEnterKey: false, allowEscapeKey: false, allowOutsideClick: false
                                                 }).then((result) => {
                                                     if (result.isConfirmed) {
                                                         window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{_entityName}}';
                                                     }
                                                 });
                                             }
                                         };
                                         check_lock_owner_request_object.send();
                                         return;
                                     }

                                     let check_do_i_still_own_entity = function() {
                                         let do_i_still_own_request_object = new XMLHttpRequest();
                                         do_i_still_own_request_object.withCredentials = true;
                                         do_i_still_own_request_object.open('GET', '{{_lockControlUrl}}?type={{_entityName}}&id={{_entityId}}&operation=do_i_still_own_lock');
                                         do_i_still_own_request_object.onreadystatechange = function() {
                                             if (this.readyState === XMLHttpRequest.DONE) {
                                                 let parsed;
                                                 let failed = this.status >= 400;
                                                 if (!failed) {
                                                     try {
                                                         parsed = JSON.parse(this.responseText);
                                                     }
                                                     catch (e) {
                                                         failed = true;
                                                     }
                                                     if (!failed) {
                                                         if (!('still_owning' in parsed)) failed = true;
                                                     }
                                                 }
                                                 if (failed) {
                                                     stop_created_intervals();
                                                     Swal.fire({title: 'Fatal error.',
                                                         html: 'Lock ownership check request has failed. Please check your network connectivity.',
                                                         showConfirmButton: true, confirmButtonText: 'Refresh the Page',
                                                         showCloseButton: false, showCancelButton: false, showDenyButton: false, allowEnterKey: false, allowEscapeKey: false, allowOutsideClick: false
                                                     }).then((result) => {
                                                         if (result.isConfirmed) {
                                                             location.reload();
                                                         }
                                                     });
                                                     return;
                                                 }
                                                 if (!parsed.still_owning) {
                                                     stop_created_intervals();
                                                     Swal.fire({title: 'Fatal error.',
                                                         html: 'The lock ownership has been lost.',
                                                         showConfirmButton: true, confirmButtonText: 'Go back',
                                                         showCloseButton: false, showCancelButton: false, showDenyButton: false, allowEnterKey: false, allowEscapeKey: false, allowOutsideClick: false
                                                     }).then((result) => {
                                                         if (result.isConfirmed) {
                                                             window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{_entityName}}';
                                                         }
                                                     });
                                                 }
                                             }
                                         };
                                         do_i_still_own_request_object.send();
                                     };

                                     created_intervals.push(setInterval(function() {
                                         check_do_i_still_own_entity();
                                     }, 2500));

                                     window.reset_inactivity_timer();
                                 }
                             };
                             lock_control_request_object.send('{}');
                         };
                         window.entity_lock_control();
                     }
                     """;
        }

        private void GenerateTitleCommonAncestors(string title, out IHtmlInputElement tnInputElement)
        {
            var card = _containerElement
                .CreateRow(_x)
                .CreateCol1OnRow(_x)
                .CreateCardOnCol(_x).Content;

            tnInputElement = _fullDocument.NotNull().CreateElement<IHtmlInputElement>();
            card.AppendChild(tnInputElement);

            tnInputElement.Type = "text";
            tnInputElement.SetAttribute("required", "");
            tnInputElement.SetAttribute("placeholder", $"Enter {title}:");
            tnInputElement.StyleElement("font-size: 2em;");

            tnInputElement.SetAttribute("oninput", "window.global_oninput(this);");
        }

        private void GenerateTitleEditField(JObject? entityTitleObject)
        {
            GenerateTitleCommonAncestors("Title", out var tnInputElement);

            tnInputElement.SetAttribute("onchange", "window.current_title_state.rendered = this.value;");

            tnInputElement.DefaultValue = (entityTitleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered) ? titleRendered : "").NotNull();
        }

        private void GenerateTagsCategoriesCommonAncestors(
            string heading,
            string entityName,
            JArray? allEntities,
            HashSet<string?> defaultSelections,
            out IHtmlDivElement headerRow,
            out IHtmlSelectElement selectElement,
            out IHtmlOptionElement blankDefaultOptionElement)
        {
            var (_, cardHeaderRow, cardContent) = _containerElement
                .CreateRow(_x)
                .CreateColFitContentRightAlignedOnRow(_x)
                .CreateCardOnCol(_x, "", heading);

            cardContent.StyleElement("min-width: 500px");

            headerRow = cardHeaderRow;

            selectElement = _fullDocument.NotNull().CreateElement<IHtmlSelectElement>();
            cardContent.AppendChild(selectElement);
            selectElement.StyleElement("max-width: 100%;");

            var refreshButtonElement = cardHeaderRow.CreateCol3OnRow(_x).AddClasses("mr-4", "mb-2").CreateButtonOnElement(_x, "Refresh", "fa-solid fa-arrows-rotate");
            refreshButtonElement.SetAttribute("onclick", $"window.refresh_relation_list(this.parentNode.querySelector('select'), '{entityName}');");

            blankDefaultOptionElement = _fullDocument.NotNull().CreateElement<IHtmlOptionElement>();
            selectElement.AppendChild(blankDefaultOptionElement);
            blankDefaultOptionElement.Value = "-1";
            blankDefaultOptionElement.Text = "";

            var sortedDic = new SortedDictionary<string, List<int>>();

            foreach (var choiceJToken in allEntities.NotNull())
            {
                var choiceJObject = (JObject)choiceJToken;

                if (!choiceJObject.TryGetTypedValue(EntityModelAttributes.Id, out int choiceId)) continue;

                if (!choiceJObject.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleJObject)
                    || !titleJObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
                    continue;

                if (sortedDic.TryGetValue(titleRendered.NotNull(), out var existing))
                {
                    existing.Add(choiceId);
                }
                else
                {
                    sortedDic.Add(titleRendered.NotNull(), [choiceId]);
                }
            }

            foreach (var (choiceNameOrTitle, choiceIds) in sortedDic)
            {
                foreach (var choiceId in choiceIds)
                {
                    var optionElement = _fullDocument.NotNull().CreateElement<IHtmlOptionElement>();
                    selectElement.AppendChild(optionElement);

                    optionElement.Value = choiceId.ToString();
                    optionElement.Text = (choiceNameOrTitle + (choiceIds.Count > 1 ? $" (Id: {choiceId})" : "")).LimitMaxCharacters(32);

                    if (defaultSelections != null && defaultSelections.Contains(optionElement.Value))
                        optionElement.SetAttribute("selected", "");
                }
            }
        }

        private void GenerateTaxonomyEditField(string taxonomyEntityName, List<int> currentTaxonomyIdsOfType, bool canCreateNewTaxonomyOfType)
        {
            var config = RfConfiguration.EntityNameToConfiguration[taxonomyEntityName].EntityConfiguration;
            var taxonomyName = config.EntityName;
            var taxonomyNameSingular = config.EntityReadableNameSingular;
            var loweredTaxonomyName = taxonomyName.ToLower();

            var taxonomyEntities = taxonomyEntityName == RfReservedEntities.TagsEntityName
                ? RfConfiguration.TagEntitiesCache.FindEntitiesAndGetCopiesAsJArray()
                : RfConfiguration.CategoryEntitiesCache.FindEntitiesAndGetCopiesAsJArray();

            var currentTaxonomyIdsOfTypeStringSet = new HashSet<string?>();
            foreach (var id in currentTaxonomyIdsOfType)
                currentTaxonomyIdsOfTypeStringSet.Add(id.ToString());

            GenerateTagsCategoriesCommonAncestors(
                taxonomyName,
                taxonomyEntityName,
                taxonomyEntities,
                currentTaxonomyIdsOfTypeStringSet,
                out var headerRow,
                out var element,
                out var blankDefaultOption);
            element.IsMultiple = true;

            if (currentTaxonomyIdsOfType.Count == 0)
            {
                blankDefaultOption.SetAttribute("selected", "");
            }

            element.SetAttribute("onchange", $"window.current_{loweredTaxonomyName}_state.array = Array.from(this.selectedOptions).map(({{ value }}) => Number(value)).filter(value => value !== -1);");

            if (!canCreateNewTaxonomyOfType) return;
            var createHintHeading = headerRow.CreateCol3OnRow(_x).AddClasses("mr-4", "mb-2").CreateButtonOnElement(_x, $"New {taxonomyNameSingular}", "fa-solid fa-circle-plus").AddClasses("text-nowrap");
            createHintHeading.Href = $"{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}?type={taxonomyEntityName}&id=new";
            createHintHeading.Target = "_blank";
        }

        private async Task GenerateParentEditField(int currentParentId, CancellationToken cancellationToken)
        {
            var getAllResult = await _operationState.GetAllEntitiesInOperationAsync(_entityName, cancellationToken);
            var entities = !getAllResult.IsSuccessful ? [] : getAllResult.Data;

            var defaultSelection = currentParentId is -1 or 0 ? null : currentParentId.ToString();

            GenerateTagsCategoriesCommonAncestors("Parent", _entityName, entities, [defaultSelection], out _, out var element, out var blankDefaultOption);

            if (defaultSelection == null)
            {
                blankDefaultOption.SetAttribute("selected", "");
            }

            element.SetAttribute("onchange", "window.current_parent_state.id = Number(this.value);");
        }
    }
}
