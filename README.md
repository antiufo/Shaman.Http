# Shaman.Http

Library for HTML/JSON/HTTP/data extraction. Based on [Shaman.Dom](https://github.com/antiufo/Shaman.Dom), [Shaman.Fizzler](https://github.com/antiufo/Shaman.Fizzler) and JSON.NET.

```csharp
using Shaman;
using Shaman.Runtime;

HtmlNode page = await "http://example.com/".AsUri().GetHtmlNodeAsync();

page.FindSingle(".header");
page.FindAll(".item").Select(x => 
{
    string title = x.GetValue(".title");
    string author = x.GetValue(".byline", regex: @"posted by (.*)");
    Uri url = x.GetLinkUrl("a");
})
```

## Metaparameters
Metaparameters can be used to easily represents additional parameters for the HTTP request:
```csharp
"http://example.com/#$metaparameter=value".AsUri().GetHtmlNodeAsync();
```
| Metaparameter   | Description |
|---|-------|
`$post-fieldName`|Sets the value for a form POST
`$json-post-user.name`|Sets the value for `{"user": {"name": …}}` and sends as POST
`$cookie-cookieName`|Sets the value for cookie `cookieName`
`$method`|Defines the HTTP method (`POST` is automatically used if POST fields are specified)
`$header-X-Requested-With`|Sets an HTTP header
`$json-token=initializePage(`|Skips to the specified token, and starts parsing JSON from ther
`$allow-redir=0`|Forbids redirects
`$timeout`|Sets a timeout, in milliseconds
`$formbutton=mybutton`|Simulates a press on the specified element and returns the resulting page
`$form-fieldName`|Sets a form field value before submitting the page with `$formbutton`
`$assert-selector=h1:contains('Results')`|Specifies a selector that must match at least one element in the page, otherwise an exception is thrown
`$forbid-selector=.overquota`|Ensures the specified selector does not match any element on the page, otherwise an exception is thrown
`$formbid-redirect-match=/overquota`|Ensures the page does not redirect to a page whose URL matches the specified regex
`$response-encoding=ISO-8859-1`|Ignores and overrides the encoding specified by the server
`$content-type=application/json`|Ignores and overrides the content-type specified by the server
`$assume-text=1`|Returns a single text node containing the unparsed text of the page
`$json-wrapped-html=response`|Parses the actual HTML inside the `response` JSON field of the response (eg. when using AJAX)
`$html-wrapped-json=script`|Parses the actual JSON inside the `script` element of the response
`$follownoscript=1`|Follows redirects inside `<noscript>` elements 

## Selectors
| Selector   | Description |
|------------|-------------|
`.cls, #id, :has…`|[Standard CSS/JQuery selectors](https://github.com/antiufo/Shaman.Fizzler)
`b:select-parent`|	Selects the parent(s) of the matched node(s)
`div[attr%='[0-9]*']` |	Elements whose attr attribute matches the specified regex
`span:matches('ab?')` |	Elements whose inner text matches the specified regex
`/div`|	Performs the initial selection at the top level of the search context instead of the descendant nodes. For example, `node.QuerySelector("/:select-parent") == node.ParentNode`. Without the slash, the result would be "the parent of the first descendant", probably not what you want.
`body:split-after(hr)` |	Groups the children of `<body>` into a pseudo-element every time a `<hr>` is found. Each `<hr>` will be the first child of its own group. Nodes before the first `<hr>` will be ignored. Note that the sub-selector (`hr`) must only match direct children of the context node. You may want to use `body:split-after(/* > hr)` to force this behavior (see the previous selector)
`body:split-before(hr)`|	Similar to the previous one, except that every `<hr>` will be the last of its own group. Nodes after the last `<hr>` will be ignored.
`body:split-between(hr)`|	Similar to the previous one, except that only content between two `<hr>`s will be included. `<hr>`s themselves won't be part of the groups.
`body:split-all(hr)`|	Similar to the previous one, except that content before the first `<hr>` and after the last `<hr>` will be included too.
`.main:before(hr)`| 	Selects the children of .main preceding the first `<hr>` child, and groups them into a single pseudo-element (`<hr>` is excluded).
`.main:after(h1)`|	Selects the children of .main following the first `<h1>` child, and groups them into a single pseudo-element (`<h1>` is excluded).
`.main:between(h1; hr)`|	Selects the children of .main between the first `<h1>` child and the first following `<hr>` (possibly the same element), grouping them into a single pseudo-element. `<h1>` and `<hr>` are not part of the group. Note the semicolon (`;`) used to separate the two parameters.
`:last`|	Selects the last matched element
`:heading-content(h2:contains('Users'))`|	Groups the next siblings of the specified `<h2>` node into a new pseudo-element, up to the following `<h2>` or `<h1>` (if any)
`tr:nth-cell(3)`|	Returns the `n`th cell (zero based) of a table row, taking `colspan` attributes into account
`li:skip(2)`|	Skips the first 2 matched nodes
`tr:skip-last(2)`|	Skips the last 2 matched nodes
`:ldjson`|Takes JSON-LD tags and parses them as JSON
`:property('name')`|Returns the value of `<meta>` tags with the given `itemprop`, `name` or `property`
`:find-attribute('name')`|Returns the values of `name` attributes for elements with that attribute
`:text-between('before', 'after')`|Returns the text between the given strings, for each element
`:text-before('after')`|Returns the text before the given string, for each element
`:text-after('before')`|Returns the text after the given string, for each element
`:reparse-html`|Takes an element containing an HTML string (eg. from JSON) and parses it, see below
`:reparse-attr-html('name')`|Same as above, but with an attribute (eg. `title="<div></div>"`), see below
`:text-is('value')`|Nodes whose trimmed text is '`value`'
`:direct-text-is('value')`|Faster version of the previous, only matches direct content, ignoring subelements
`:either(sel1; sel2)`|Merges the results of two selectors
`:first-after-text('value')`|Inside each `div`, takes the child element that immediately follows trimmed text '`value`'
`:split-text-lines`|Takes the string content of each node, splits them by lines, and returns each line.
`:split-text('separator')`|Takes the string content of each node, splits them, and returns each component.
`:following-text`|Takes the text that directly follows each matched node.
`:previous-text`|Takes the text that directly preceeds each matched node.
`:select-ancestor(4)`|Selects the `n`th-ancestor. 1 means the parent.
`:reparse-json`|Takes an element containing JSON data and parses it. Not necessary if the root page is already JSON.
`:json-attr('name')`|Parses a JSON attribute (eg. `data-something="{val: 1}"`), see below
`:json-attr-token('name', 'load(')`|Parses a JSON attribute, skipping to a token (eg. `onclick="load({val: 1})"`), see below
`:json-token('load(')`|Parses JSON data, skipping to a specific token, see below
`:before-weak(.limit)`|Wraps the nodes that preceed the `.limit` child. If it does not exist, an empty node is returned.
`:except-children(.bad)`|Returns the original nodes, but without `.bad` children
`:take-while(li)`|Returns the original nodes, but stops when a non-`li` is found
`:take-until(li)`|Returns the original nodes, but stops when a `li` is found
`:skip-while(li)`|Skips the original nodes and resumes when a non-`li` is found
`:skip-until(li)`|Skips the original nodes and resumes when a `li` is found
`:take(5)`|Takes the first 5 items
`:skip(5)`|Skips the first 5 items
`:take-last(5)`|Takes the last 5 items
`:skip-last(5)`|Skips the last 5 items
`h2:heading-content`|Returns the wrapped nodes after the given h2, until another `h2` (or `h1` is found)
`:merge`|Wraps all the results together in a single node

## JSON selectors
It is possible to navigate JSON structures using selectors. Note however that tag names must be written in lower case, regardless of how they are in the real JSON. Some of the values you need are often found inside `data-*` attributes or JavaScript event handlers.

The following selectors make it possible to navigate inside the JSON structures of these nodes:

```
a:json-attr-token('onclick', 'showDetails(') > name
a:json-attr-token('onclick', 'showDetails(') > info > phone
```
```html
<a href="#" onclick="showDetails({name: 'John Doe', info: {phone: '555-1212'}})">Show details</a>
```

The `showDetails(` token is searched textually inside the specified attribute, then the JSON code is parsed till its end is detected, and the remaining JavaScript code is ignored. Additionally, `script:json-token('var data =')` can be used for extracting JSON structures from `<script>` nodes, and `div:json-attr('data-info')` when the attribute itself is already a valid JSON structure. If the element directly contains JSON data, use `script:reparse-json > val`

## HTML reparsing
Sometimes you might have some HTML code inside an HTML attribute itself, or inside of a JSON string. In this case, you can navigate inside the inner HTML using `:reparse-html`.

```html
<img title="<div class=popup><span class=votes>5</span></div>" src="/images/1975691.jpg">
```

```img:reparse-html-attr('title') > .votes```

```json
{ "myjson": { "description": "<h1 class=title>Introduction</h1>" } }
```

```myjson > description:reparse-html > h1.title```

## Caching
For debugging/testing purposes, it might be useful to enable caching:
```csharp
Caching.EnableWebCache("C:\\Cache");
```

## WebFile
Provides simple access for downloading files, handling name collisions (skip, overwrite, rename), progress: `Shaman.Types.WebFile`. The URL syntax also support metaparameters (see above).

