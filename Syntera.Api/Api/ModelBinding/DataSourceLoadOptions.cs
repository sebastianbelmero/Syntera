using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Syntera.Api.ModelBinding;

/// <summary>
/// Receives DevExtreme data processing options (filter, sort, group, paging,
/// summaries) sent by the <c>devextreme-aspnet-data-nojquery</c> client used
/// by the AppGrid frontend.
///
/// NOTE: <c>DataSourceLoadOptions</c> is intentionally NOT part of the
/// <c>DevExtreme.AspNet.Data</c> NuGet package. DevExpress ships it as a
/// project-local sample that each consumer owns:
/// https://github.com/DevExpress/DevExtreme.AspNet.Data/blob/master/net/Sample/DataSourceLoadOptions.cs
/// The <see cref="ModelBinderAttribute"/> wires the parameter to the binder
/// below, so no DI registration is required.
/// </summary>
[ModelBinder(BinderType = typeof(DataSourceLoadOptionsBinder))]
public sealed class DataSourceLoadOptions : DataSourceLoadOptionsBase
{
}

/// <summary>
/// Binds DevExtreme load options from the request value providers (query
/// string for GET loads, form body for POST loads). Each option arrives as a
/// single value per key — the client JSON-serializes compound options
/// (<c>filter</c>, <c>sort</c>, <c>group</c>, <c>totalSummary</c>, ...) — and
/// <see cref="DataSourceLoadOptionsParser"/> converts them into the typed
/// properties on <see cref="DataSourceLoadOptionsBase"/>.
/// </summary>
public sealed class DataSourceLoadOptionsBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var loadOptions = new DataSourceLoadOptions();
        DataSourceLoadOptionsParser.Parse(
            loadOptions,
            key => bindingContext.ValueProvider.GetValue(key).FirstOrDefault());
        bindingContext.Result = ModelBindingResult.Success(loadOptions);
        return Task.CompletedTask;
    }
}
