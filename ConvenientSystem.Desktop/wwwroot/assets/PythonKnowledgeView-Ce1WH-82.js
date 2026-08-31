var ot=Object.defineProperty;var lt=(n,e,r)=>e in n?ot(n,e,{enumerable:!0,configurable:!0,writable:!0,value:r}):n[e]=r;var D=(n,e,r)=>lt(n,typeof e!="symbol"?e+"":e,r);import{bL as ct,d as pt,C as ut,o as dt,aQ as ft,ag as ht,c as ae,e as U,f as oe,w as me,F as Ze,r as un,n as Xe,t as Te,af as Hn,aF as gt,p as _t,s as we,E as mt,k as bt,J as yt,aw as xt,b as le,l as dn,Y as fn,m as kt,bf as wt,X as jn,z as Et}from"./index-DFyRv3wd.js";/* empty css                   */function bn(){return{async:!1,breaks:!1,extensions:null,gfm:!0,hooks:null,pedantic:!1,renderer:null,silent:!1,tokenizer:null,walkTokens:null}}var Pe=bn();function rr(n){Pe=n}var Re={exec:()=>null};function Oe(n){let e=[];return r=>{let t=Math.max(0,Math.min(3,r-1)),s=e[t];return s||(s=n(t),e[t]=s),s}}function O(n,e=""){let r=typeof n=="string"?n:n.source,t={replace:(s,o)=>{let l=typeof o=="string"?o:o.source;return l=l.replace(ee.caret,"$1"),r=r.replace(s,l),t},getRegex:()=>new RegExp(r,e)};return t}var vt=((n="")=>{try{return!!new RegExp("(?<=1)(?<!1)"+n)}catch{return!1}})(),ee={codeRemoveIndent:/^(?: {1,4}| {0,3}\t)/gm,outputLinkReplace:/\\([\[\]])/g,indentCodeCompensation:/^(\s+)(?:```)/,beginningSpace:/^\s+/,endingHash:/#$/,startingSpaceChar:/^ /,endingSpaceChar:/ $/,nonSpaceChar:/[^ ]/,newLineCharGlobal:/\n/g,tabCharGlobal:/\t/g,multipleSpaceGlobal:/\s+/g,blankLine:/^[ \t]*$/,doubleBlankLine:/\n[ \t]*\n[ \t]*$/,blockquoteStart:/^ {0,3}>/,blockquoteSetextReplace:/\n {0,3}((?:=+|-+) *)(?=\n|$)/g,blockquoteSetextReplace2:/^ {0,3}>[ \t]?/gm,listReplaceNesting:/^ {1,4}(?=( {4})*[^ ])/g,listIsTask:/^\[[ xX]\] +\S/,listReplaceTask:/^\[[ xX]\] +/,listTaskCheckbox:/\[[ xX]\]/,anyLine:/\n.*\n/,hrefBrackets:/^<(.*)>$/,tableDelimiter:/[:|]/,tableAlignChars:/^\||\| *$/g,tableRowBlankLine:/\n[ \t]*$/,tableAlignRight:/^ *-+: *$/,tableAlignCenter:/^ *:-+: *$/,tableAlignLeft:/^ *:-+ *$/,startATag:/^<a /i,endATag:/^<\/a>/i,startPreScriptTag:/^<(pre|code|kbd|script)(\s|>)/i,endPreScriptTag:/^<\/(pre|code|kbd|script)(\s|>)/i,startAngleBracket:/^</,endAngleBracket:/>$/,pedanticHrefTitle:/^([^'"]*[^\s])\s+(['"])(.*)\2/,unicodeAlphaNumeric:/[\p{L}\p{N}]/u,escapeTest:/[&<>"']/,escapeReplace:/[&<>"']/g,escapeTestNoEncode:/[<>"']|&(?!(#\d{1,7}|#[Xx][a-fA-F0-9]{1,6}|\w+);)/,escapeReplaceNoEncode:/[<>"']|&(?!(#\d{1,7}|#[Xx][a-fA-F0-9]{1,6}|\w+);)/g,caret:/(^|[^\[])\^/g,percentDecode:/%25/g,findPipe:/\|/g,splitPipe:/ \|/,slashPipe:/\\\|/g,carriageReturn:/\r\n|\r/g,spaceLine:/^ +$/gm,notSpaceStart:/^\S*/,endingNewline:/\n$/,listItemRegex:n=>new RegExp(`^( {0,3}${n})((?:[	 ][^\\n]*)?(?:\\n|$))`),nextBulletRegex:Oe(n=>new RegExp(`^ {0,${n}}(?:[*+-]|\\d{1,9}[.)])((?:[ 	][^\\n]*)?(?:\\n|$))`)),hrRegex:Oe(n=>new RegExp(`^ {0,${n}}((?:- *){3,}|(?:_ *){3,}|(?:\\* *){3,})(?:\\n+|$)`)),fencesBeginRegex:Oe(n=>new RegExp(`^ {0,${n}}(?:\`\`\`|~~~)`)),headingBeginRegex:Oe(n=>new RegExp(`^ {0,${n}}#`)),htmlBeginRegex:Oe(n=>new RegExp(`^ {0,${n}}<(?:[a-z].*>|!--)`,"i")),blockquoteBeginRegex:Oe(n=>new RegExp(`^ {0,${n}}>`))},St=/^(?:[ \t]*(?:\n|$))+/,Tt=/^((?: {4}| {0,3}\t)[^\n]+(?:\n(?:[ \t]*(?:\n|$))*)?)+/,Rt=/^ {0,3}(`{3,}(?=[^`\n]*(?:\n|$))|~{3,})([^\n]*)(?:\n|$)(?:|([\s\S]*?)(?:\n|$))(?: {0,3}\1[~`]* *(?=\n|$)|$)/,$e=/^ {0,3}((?:-[\t ]*){3,}|(?:_[ \t]*){3,}|(?:\*[ \t]*){3,})(?:\n+|$)/,At=/^ {0,3}(#{1,6})(?=\s|$)(.*)(?:\n+|$)/,yn=/ {0,3}(?:[*+-]|\d{1,9}[.)])/,tr=/^(?!bull |blockCode|fences|blockquote|heading|html|table)((?:.|\n(?!\s*?\n|bull |blockCode|fences|blockquote|heading|html|table))+?)\n {0,3}(=+|-+) *(?:\n+|$)/,sr=O(tr).replace(/bull/g,yn).replace(/blockCode/g,/(?: {4}| {0,3}\t)/).replace(/fences/g,/ {0,3}(?:`{3,}|~{3,})/).replace(/blockquote/g,/ {0,3}>/).replace(/heading/g,/ {0,3}#{1,6}(?:\s|$)/).replace(/html/g,/ {0,3}<[^\n>]+>\n/).replace(/\|table/g,"").getRegex(),Pt=O(tr).replace(/bull/g,yn).replace(/blockCode/g,/(?: {4}| {0,3}\t)/).replace(/fences/g,/ {0,3}(?:`{3,}|~{3,})/).replace(/blockquote/g,/ {0,3}>/).replace(/heading/g,/ {0,3}#{1,6}(?:\s|$)/).replace(/html/g,/ {0,3}<[^\n>]+>\n/).replace(/table/g,/ {0,3}\|?(?:[:\- ]*\|)+[\:\- ]*\n/).getRegex(),xn=/^([^\n]+(?:\n(?!hr|heading|lheading|blockquote|fences|list|html|table|[ \t]+\n)[^\n]+)*)/,Ct=/^[^\n]+/,kn=/(?!\s*\])(?:\\[\s\S]|[^\[\]\\])+/,Nt=O(/^ {0,3}\[(label)\]: *(?:\n[ \t]*)?([^<\s][^\s]*|<.*?>)(?:(?: +(?:\n[ \t]*)?| *\n[ \t]*)(title))? *(?:\n+|$)/).replace("label",kn).replace("title",/(?:"(?:\\"?|[^"\\])*"|'[^'\n]*(?:\n[^'\n]+)*\n?'|\([^()]*\))/).getRegex(),Ot=O(/^(bull)([ \t][^\n]*?)?(?:\n|$)/).replace(/bull/g,yn).getRegex(),rn="address|article|aside|base|basefont|blockquote|body|caption|center|col|colgroup|dd|details|dialog|dir|div|dl|dt|fieldset|figcaption|figure|footer|form|frame|frameset|h[1-6]|head|header|hr|html|iframe|legend|li|link|main|menu|menuitem|meta|nav|noframes|ol|optgroup|option|p|param|search|section|summary|table|tbody|td|tfoot|th|thead|title|tr|track|ul",wn=/<!--(?:-?>|[\s\S]*?(?:-->|$))/,It=O("^ {0,3}(?:<(script|pre|style|textarea)[\\s>][\\s\\S]*?(?:</\\1>[^\\n]*\\n*|$)|comment[^\\n]*(\\n+|$)|<\\?[\\s\\S]*?(?:\\?>[^\\n]*\\n*|$)|<![A-Z][\\s\\S]*?(?:>[^\\n]*\\n*|$)|<!\\[CDATA\\[[\\s\\S]*?(?:\\]\\]>[^\\n]*\\n*|$)|</?(tag)(?: +|\\n|/?>)[\\s\\S]*?(?:(?:\\n[ 	]*)+\\n|$)|<(?!script|pre|style|textarea)([a-z][\\w-]*)(?:attribute)*? */?>(?=[ \\t]*(?:\\n|$))[\\s\\S]*?(?:(?:\\n[ 	]*)+\\n|$)|</(?!script|pre|style|textarea)[a-z][\\w-]*\\s*>(?=[ \\t]*(?:\\n|$))[\\s\\S]*?(?:(?:\\n[ 	]*)+\\n|$))","i").replace("comment",wn).replace("tag",rn).replace("attribute",/ +[a-zA-Z:_][\w.:-]*(?: *= *"[^"\n]*"| *= *'[^'\n]*'| *= *[^\s"'=<>`]+)?/).getRegex(),ir=n=>O(xn).replace("hr",$e).replace("heading"," {0,3}#{1,6}(?:\\s|$)").replace("|lheading","").replace("|table","").replace("blockquote"," {0,3}>").replace("fences"," {0,3}(?:`{3,}(?=[^`\\n]*\\n)|~~~)[^\\n]*\\n").replace("list",n).replace("html","</?(?:tag)(?: +|\\n|/?>)|<(?:script|pre|style|textarea|!--)").replace("tag",rn).getRegex(),Mt=ir(/ {0,3}(?:[*+-]|1[.)])[ \t]+[^ \t\n]/),Lt=ir(/ {0,3}(?:[*+-]|\d{1,9}[.)])(?:[ \t]|\n|$)/),Dt=O(/^( {0,3}> ?(paragraph|[^\n]*)(?:\n|$))+/).replace("paragraph",Lt).getRegex(),En={blockquote:Dt,code:Tt,def:Nt,fences:Rt,heading:At,hr:$e,html:It,lheading:sr,list:Ot,newline:St,paragraph:Mt,table:Re,text:Ct},Un=O("^ *([^\\n ].*)\\n {0,3}((?:\\| *)?:?-+:? *(?:\\| *:?-+:? *)*(?:\\| *)?)(?:\\n((?:(?! *\\n|hr|heading|blockquote|code|fences|list|html).*(?:\\n|$))*)\\n*|$)").replace("hr",$e).replace("heading"," {0,3}#{1,6}(?:\\s|$)").replace("blockquote"," {0,3}>").replace("code","(?: {4}| {0,3}	)[^\\n]").replace("fences"," {0,3}(?:`{3,}(?=[^`\\n]*\\n)|~~~)[^\\n]*\\n").replace("list"," {0,3}(?:[*+-]|1[.)])[ \\t]").replace("html","</?(?:tag)(?: +|\\n|/?>)|<(?:script|pre|style|textarea|!--)").replace("tag",rn).getRegex(),$t={...En,lheading:Pt,table:Un,paragraph:O(xn).replace("hr",$e).replace("heading"," {0,3}#{1,6}(?:\\s|$)").replace("|lheading","").replace("table",Un).replace("blockquote"," {0,3}>").replace("fences"," {0,3}(?:`{3,}(?=[^`\\n]*\\n)|~~~)[^\\n]*\\n").replace("list"," {0,3}(?:[*+-]|1[.)])[ \\t]+[^ \\t\\n]").replace("html","</?(?:tag)(?: +|\\n|/?>)|<(?:script|pre|style|textarea|!--)").replace("tag",rn).getRegex()},Bt={...En,html:O(`^ *(?:comment *(?:\\n|\\s*$)|<(tag)[\\s\\S]+?</\\1> *(?:\\n{2,}|\\s*$)|<tag(?:"[^"]*"|'[^']*'|\\s[^'"/>\\s]*)*?/?> *(?:\\n{2,}|\\s*$))`).replace("comment",wn).replace(/tag/g,"(?!(?:a|em|strong|small|s|cite|q|dfn|abbr|data|time|code|var|samp|kbd|sub|sup|i|b|u|mark|ruby|rt|rp|bdi|bdo|span|br|wbr|ins|del|img)\\b)\\w+(?!:|[^\\w\\s@]*@)\\b").getRegex(),def:/^ *\[([^\]]+)\]: *<?([^\s>]+)>?(?: +(["(][^\n]+[")]))? *(?:\n+|$)/,heading:/^(#{1,6})(.*)(?:\n+|$)/,fences:Re,lheading:/^(.+?)\n {0,3}(=+|-+) *(?:\n+|$)/,paragraph:O(xn).replace("hr",$e).replace("heading",` *#{1,6} *[^
]`).replace("lheading",sr).replace("|table","").replace("blockquote"," {0,3}>").replace("|fences","").replace("|list","").replace("|html","").replace("|tag","").getRegex()},zt=/^\\([!"#$%&'()*+,\-./:;<=>?@\[\]\\^_`{|}~])/,qt=/^(`+)([^`]|[^`][\s\S]*?[^`])\1(?!`)/,ar=/^( {2,}|\\)\n(?!\s*$)/,Ft=/^(`+|[^`])(?:(?= {2,}\n)|[\s\S]*?(?:(?=[\\<!\[`*_]|\b_|$)|[^ ](?= {2,}\n)))/,be=/[\p{P}\p{S}]/u,Ie=/[\s\p{P}\p{S}]/u,Be=/[^\s\p{P}\p{S}]/u,Ht=O(/^((?![*_])punctSpace)/,"u").replace(/punctSpace/g,Ie).getRegex(),jt=/[\p{Pi}\p{Ps}"']/u,or=/(?!~)[\p{P}\p{S}]/u,Ut=/(?!~)[\s\p{P}\p{S}]/u,Gt=/(?:[^\s\p{P}\p{S}]|~)/u,Wt=O(/link|precode-code|html/,"g").replace("link",/\[(?:[^\[\]`]|(?<a>`+)[^`]+\k<a>(?!`))*?\]\((?:\\[\s\S]|[^\\\(\)]|\((?:\\[\s\S]|[^\\\(\)])*\))*\)/).replace("precode-",vt?"(?<!`)()":"(^^|[^`])").replace("code",/(?<b>`+)[^`]+\k<b>(?!`)/).replace("html",/<(?! )[^<>]*?>/).getRegex(),lr=/^(?:\*+(?:((?!\*)punct)|([^\s*]))?)|^_+(?:((?!_)punct)|([^\s_]))?/,Kt=O(lr,"u").replace(/punct/g,be).getRegex(),Vt=O(lr,"u").replace(/punct/g,or).getRegex(),Zt=/^(?:\*+(?:((?!\*)(?!openQuote)punct)|([^\s*]))?)|^_+(?:((?!_)(?!openQuote)punct)|([^\s_]))?/,Xt=O(Zt,"u").replace(/openQuote/g,jt).replace(/punct/g,be).getRegex(),cr="^[^_*]*?__[^_*]*?\\*[^_*]*?(?=__)|[^*]+(?=[^*])|(?!\\*)punct(\\*+)(?=[\\s]|$)|notPunctSpace(\\*+)(?!\\*)(?=punctSpace|$)|(?!\\*)punctSpace(\\*+)(?=notPunctSpace)|[\\s](\\*+)(?!\\*)(?=punct)|(?!\\*)punct(\\*+)(?!\\*)(?=punct)|notPunctSpace(\\*+)(?=notPunctSpace)",Qt=O(cr,"gu").replace(/notPunctSpace/g,Be).replace(/punctSpace/g,Ie).replace(/punct/g,be).getRegex(),Yt=O(cr,"gu").replace(/notPunctSpace/g,Gt).replace(/punctSpace/g,Ut).replace(/punct/g,or).getRegex(),Jt="^[^_*]*?__[^_*]*?\\*[^_*]*?(?=__)|[^*]+(?=[^*])|(?!\\*)punct(\\*+)(?=[\\s]|$)|notPunctSpace(\\*+)(?!\\*)(?=punctSpace|$)|(?!\\*)[\\s](\\*+)(?=notPunctSpace)|[\\s](\\*+)(?!\\*)(?=punct)|(?!\\*)punct(\\*+)(?!\\*)(?=punct)|(?:(?!\\*)punct|notPunctSpace)(\\*+)(?!\\*)(?=notPunctSpace)",es=O(Jt,"gu").replace(/notPunctSpace/g,Be).replace(/punctSpace/g,Ie).replace(/punct/g,be).getRegex(),ns=O("^[^_*]*?\\*\\*[^_*]*?_[^_*]*?(?=\\*\\*)|[^_]+(?=[^_])|(?!_)punct(_+)(?=[\\s]|$)|notPunctSpace(_+)(?!_)(?=punctSpace|$)|(?!_)punctSpace(_+)(?=notPunctSpace)|[\\s](_+)(?!_)(?=punct)|(?!_)punct(_+)(?!_)(?=punct)","gu").replace(/notPunctSpace/g,Be).replace(/punctSpace/g,Ie).replace(/punct/g,be).getRegex(),rs="^[^_*]*?\\*\\*[^_*]*?_[^_*]*?(?=\\*\\*)|[^_]+(?=[^_])|(?!_)punct(_+)(?=[\\s]|$)|notPunctSpace(_+)(?!_)(?=punctSpace|$)|(?!_)[\\s](_+)(?=notPunctSpace)|[\\s](_+)(?!_)(?=punct)|(?!_)punct(_+)(?!_)(?=punct)|(?:(?!_)punct|notPunctSpace)(_+)(?!_)(?=notPunctSpace)",ts=O(rs,"gu").replace(/notPunctSpace/g,Be).replace(/punctSpace/g,Ie).replace(/punct/g,be).getRegex(),ss=O(/^~~?(?:((?!~)punct)|[^\s~])/,"u").replace(/punct/g,be).getRegex(),is="^[^~]+(?=[^~])|(?!~)punct(~~?)(?=[\\s]|$)|notPunctSpace(~~?)(?!~)(?=punctSpace|$)|(?!~)punctSpace(~~?)(?=notPunctSpace)|[\\s](~~?)(?!~)(?=punct)|(?!~)punct(~~?)(?!~)(?=punct)|notPunctSpace(~~?)(?=notPunctSpace)",as=O(is,"gu").replace(/notPunctSpace/g,Be).replace(/punctSpace/g,Ie).replace(/punct/g,be).getRegex(),os=O(/\\(punct)/,"gu").replace(/punct/g,be).getRegex(),ls=O(/^<(scheme:[^\s\x00-\x1f<>]*|email)>/).replace("scheme",/[a-zA-Z][a-zA-Z0-9+.-]{1,31}/).replace("email",/[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+(@)[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(?![-_])/).getRegex(),cs=O(wn).replace("(?:-->|$)","-->").getRegex(),ps=O("^comment|^</[a-zA-Z][\\w:-]*\\s*>|^<[a-zA-Z][\\w-]*(?:attribute)*?\\s*/?>|^<\\?[\\s\\S]*?\\?>|^<![a-zA-Z]+\\s[\\s\\S]*?>|^<!\\[CDATA\\[[\\s\\S]*?\\]\\]>").replace("comment",cs).replace("attribute",/\s+[a-zA-Z:_][\w.:-]*(?:\s*=\s*"[^"]*"|\s*=\s*'[^']*'|\s*=\s*[^\s"'=<>`]+)?/).getRegex(),Je=/(?:\[(?:\\[\s\S]|[^\[\]\\])*\]|\\[\s\S]|`+(?!`)[^`]*?`+(?!`)|``+(?=\])|[^\[\]\\`])*?/,us=O(/^!?\[(label)\]\(\s*(href)(?:(?:[ \t]+(?:\n[ \t]*)?|\n[ \t]*)(title))?\s*\)/).replace("label",Je).replace("href",/<(?:\\.|[^\n<>\\])+>|[^ \t\n\x00-\x1f]+|(?=\))/).replace("title",/"(?:\\"?|[^"\\])*"|'(?:\\'?|[^'\\])*'|\((?:\\\)?|[^)\\])*\)/).getRegex(),pr=O(/^!?\[(label)\]\[(ref)\]/).replace("label",Je).replace("ref",kn).getRegex(),ur=O(/^!?\[(ref)\](?:\[\])?/).replace("ref",kn).getRegex(),ds=O("reflink|nolink(?!\\()","g").replace("reflink",pr).replace("nolink",ur).getRegex(),Gn=/[hH][tT][tT][pP][sS]?|[fF][tT][pP]/,vn={_backpedal:Re,anyPunctuation:os,autolink:ls,blockSkip:Wt,br:ar,code:qt,del:Re,delLDelim:Re,delRDelim:Re,emStrongLDelim:Kt,emStrongRDelimAst:Qt,emStrongRDelimUnd:ns,escape:zt,link:us,nolink:ur,punctuation:Ht,reflink:pr,reflinkSearch:ds,tag:ps,text:Ft,url:Re},fs={...vn,emStrongLDelim:Xt,emStrongRDelimAst:es,emStrongRDelimUnd:ts,link:O(/^!?\[(label)\]\((.*?)\)/).replace("label",Je).getRegex(),reflink:O(/^!?\[(label)\]\s*\[([^\]]*)\]/).replace("label",Je).getRegex()},gn={...vn,emStrongRDelimAst:Yt,emStrongLDelim:Vt,delLDelim:ss,delRDelim:as,url:O(/^((?:protocol):\/\/|www\.)(?:[a-zA-Z0-9\-]+\.?)+[^\s<]*|^email/).replace("protocol",Gn).replace("email",/[A-Za-z0-9._+-]+(@)[a-zA-Z0-9-_]+(?:\.[a-zA-Z0-9-_]*[a-zA-Z0-9])+(?![-_])/).getRegex(),_backpedal:/(?:[^?!.,:;*_'"~()&]+|\([^)]*\)|&(?![a-zA-Z0-9]+;$)|[?!.,:;*_'"~)]+(?!$))+/,del:/^(~~?)(?=[^\s~])((?:\\[\s\S]|[^\\])*?(?:\\[\s\S]|[^\s~\\]))\1(?=[^~]|$)/,text:O(/^(`+|~+|[^`~])(?:(?=[`~])|(?= {2,}\n)|(?=[a-zA-Z0-9.!#$%&'*+\/=?_`{\|}~-]+@)|[\s\S]*?(?:(?=[\\<!\[`*~_]|\b_|protocol:\/\/|www\.|$)|[^ ](?= {2,}\n)|[^a-zA-Z0-9.!#$%&'*+\/=?_`{\|}~-](?=[a-zA-Z0-9.!#$%&'*+\/=?_`{\|}~-]+@)))/).replace("protocol",Gn).getRegex()},hs={...gn,br:O(ar).replace("{2,}","*").getRegex(),text:O(gn.text).replace("\\b_","\\b_| {2,}\\n").replace(/\{2,\}/g,"*").getRegex()},Qe={normal:En,gfm:$t,pedantic:Bt},Le={normal:vn,gfm:gn,breaks:hs,pedantic:fs},gs={"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"},Wn=n=>gs[n];function he(n,e){if(e){if(ee.escapeTest.test(n))return n.replace(ee.escapeReplace,Wn)}else if(ee.escapeTestNoEncode.test(n))return n.replace(ee.escapeReplaceNoEncode,Wn);return n}function Kn(n){try{n=encodeURI(n).replace(ee.percentDecode,"%")}catch{return null}return n}function Vn(n,e){var o;let r=n.replace(ee.findPipe,(l,c,a)=>{let f=!1,u=c;for(;--u>=0&&a[u]==="\\";)f=!f;return f?"|":" |"}),t=r.split(ee.splitPipe),s=0;if(t[0].trim()||t.shift(),t.length>0&&!((o=t.at(-1))!=null&&o.trim())&&t.pop(),e)if(t.length>e)t.splice(e);else for(;t.length<e;)t.push("");for(;s<t.length;s++)t[s]=t[s].trim().replace(ee.slashPipe,"|");return t}function Ee(n,e,r){let t=n.length;if(t===0)return"";let s=0;for(;s<t&&n.charAt(t-s-1)===e;)s++;return n.slice(0,t-s)}function Zn(n){let e=n.split(`
`),r=e.length-1;for(;r>=0&&ee.blankLine.test(e[r]);)r--;return e.length-r<=2?n:e.slice(0,r+1).join(`
`)}function _s(n,e){if(n.indexOf(e[1])===-1)return-1;let r=0;for(let t=0;t<n.length;t++)if(n[t]==="\\")t++;else if(n[t]===e[0])r++;else if(n[t]===e[1]&&(r--,r<0))return t;return r>0?-2:-1}function ms(n,e=0){let r=e,t="";for(let s of n)if(s==="	"){let o=4-r%4;t+=" ".repeat(o),r+=o}else t+=s,r++;return t}function Xn(n,e,r,t,s){let o=e.href,l=e.title||null,c=n[1].replace(s.other.outputLinkReplace,"$1");t.state.inLink=!0;let a={type:n[0].charAt(0)==="!"?"image":"link",raw:r,href:o,title:l,text:c,tokens:t.inlineTokens(c)};return t.state.inLink=!1,a}function bs(n,e,r){let t=n.match(r.other.indentCodeCompensation);if(t===null)return e;let s=t[1];return e.split(`
`).map(o=>{let l=o.match(r.other.beginningSpace);if(l===null)return o;let[c]=l;return c.length>=s.length?o.slice(s.length):o}).join(`
`)}var en=class{constructor(n){D(this,"options");D(this,"rules");D(this,"lexer");this.options=n||Pe}space(n){let e=this.rules.block.newline.exec(n);if(e&&e[0].length>0)return{type:"space",raw:e[0]}}code(n){let e=this.rules.block.code.exec(n);if(e){let r=this.options.pedantic?e[0]:Zn(e[0]),t=r.replace(this.rules.other.codeRemoveIndent,"");return{type:"code",raw:r,codeBlockStyle:"indented",text:t}}}fences(n){let e=this.rules.block.fences.exec(n);if(e){let r=e[0],t=bs(r,e[3]||"",this.rules);return{type:"code",raw:r,lang:e[2]?e[2].trim().replace(this.rules.inline.anyPunctuation,"$1"):e[2],text:t}}}heading(n){let e=this.rules.block.heading.exec(n);if(e){let r=e[2].trim();if(this.rules.other.endingHash.test(r)){let t=Ee(r,"#");(this.options.pedantic||!t||this.rules.other.endingSpaceChar.test(t))&&(r=t.trim())}return{type:"heading",raw:Ee(e[0],`
`),depth:e[1].length,text:r,tokens:this.lexer.inline(r)}}}hr(n){let e=this.rules.block.hr.exec(n);if(e)return{type:"hr",raw:Ee(e[0],`
`)}}blockquote(n){let e=this.rules.block.blockquote.exec(n);if(e){let r=Ee(e[0],`
`).split(`
`),t="",s="",o=[];for(;r.length>0;){let l=!1,c=[],a;for(a=0;a<r.length;a++)if(this.rules.other.blockquoteStart.test(r[a]))c.push(r[a]),l=!0;else if(!l)c.push(r[a]);else break;r=r.slice(a);let f=c.join(`
`),u=f.replace(this.rules.other.blockquoteSetextReplace,`
    $1`).replace(this.rules.other.blockquoteSetextReplace2,"");t=t?`${t}
${f}`:f,s=s?`${s}
${u}`:u;let x=this.lexer.state.top;if(this.lexer.state.top=!0,this.lexer.blockTokens(u,o,!0),this.lexer.state.top=x,r.length===0)break;let d=o.at(-1);if((d==null?void 0:d.type)==="code")break;if((d==null?void 0:d.type)==="blockquote"){let v=d,m=r.join(`
`),A=v.raw+`
`+m.replace(this.rules.other.blockquoteSetextReplace2,""),j=this.blockquote(A);o[o.length-1]=j,t=`${t}
${m}`,s=s.substring(0,s.length-v.text.length)+j.text;break}else if((d==null?void 0:d.type)==="list"){let v=d,m=v.raw+`
`+r.join(`
`),A=this.list(m);o[o.length-1]=A,t=t.substring(0,t.length-d.raw.length)+A.raw,s=s.substring(0,s.length-v.raw.length)+A.raw,r=m.substring(o.at(-1).raw.length).split(`
`);continue}}return{type:"blockquote",raw:t,tokens:o,text:s}}}list(n){let e=this.rules.block.list.exec(n);if(e){let r=e[1].trim(),t=r.length>1,s={type:"list",raw:"",ordered:t,start:t?+r.slice(0,-1):"",loose:!1,items:[]};r=t?`\\d{1,9}\\${r.slice(-1)}`:`\\${r}`,this.options.pedantic&&(r=t?r:"[*+-]");let o=this.rules.other.listItemRegex(r),l=!1;for(;n;){let a=!1,f="",u="";if(!(e=o.exec(n))||this.rules.block.hr.test(n))break;f=e[0],n=n.substring(f.length);let x=ms(e[2].split(`
`,1)[0],e[1].length),d=n.split(`
`,1)[0],v=!x.trim(),m=0;if(this.options.pedantic?(m=2,u=x.trimStart()):v?m=e[1].length+1:(m=x.search(this.rules.other.nonSpaceChar),m=m>4?1:m,u=x.slice(m),m+=e[1].length),v&&this.rules.other.blankLine.test(d)&&(f+=d+`
`,n=n.substring(d.length+1),a=!0),!a){let A=this.rules.other.nextBulletRegex(m),j=this.rules.other.hrRegex(m),G=this.rules.other.fencesBeginRegex(m),ne=this.rules.other.headingBeginRegex(m),ue=this.rules.other.htmlBeginRegex(m),P=this.rules.other.blockquoteBeginRegex(m);for(;n;){let b=n.split(`
`,1)[0],C;if(d=b,this.options.pedantic?(d=d.replace(this.rules.other.listReplaceNesting,"  "),C=d):C=d.replace(this.rules.other.tabCharGlobal,"    "),G.test(d)||ne.test(d)||ue.test(d)||P.test(d)||A.test(d)||j.test(d))break;if(C.search(this.rules.other.nonSpaceChar)>=m||!d.trim())u+=`
`+C.slice(m);else{if(v||x.replace(this.rules.other.tabCharGlobal,"    ").search(this.rules.other.nonSpaceChar)>=4||G.test(x)||ne.test(x)||j.test(x))break;u+=`
`+d}v=!d.trim(),f+=b+`
`,n=n.substring(b.length+1),x=C.slice(m)}}s.loose||(l?s.loose=!0:this.rules.other.doubleBlankLine.test(f)&&(l=!0)),s.items.push({type:"list_item",raw:f,task:!!this.options.gfm&&this.rules.other.listIsTask.test(u),loose:!1,text:u,tokens:[]}),s.raw+=f}let c=s.items.at(-1);if(c)c.raw=c.raw.trimEnd(),c.text=c.text.trimEnd();else return;s.raw=s.raw.trimEnd();for(let a of s.items){this.lexer.state.top=!1,a.tokens=this.lexer.blockTokens(a.text,[]);let f=a.tokens[0];if(a.task&&((f==null?void 0:f.type)==="text"||(f==null?void 0:f.type)==="paragraph")){a.text=a.text.replace(this.rules.other.listReplaceTask,""),f.raw=f.raw.replace(this.rules.other.listReplaceTask,""),f.text=f.text.replace(this.rules.other.listReplaceTask,"");for(let x=this.lexer.inlineQueue.length-1;x>=0;x--)if(this.rules.other.listIsTask.test(this.lexer.inlineQueue[x].src)){this.lexer.inlineQueue[x].src=this.lexer.inlineQueue[x].src.replace(this.rules.other.listReplaceTask,"");break}let u=this.rules.other.listTaskCheckbox.exec(a.raw);if(u){let x={type:"checkbox",raw:u[0]+" ",checked:u[0]!=="[ ]"};a.checked=x.checked,s.loose?a.tokens[0]&&["paragraph","text"].includes(a.tokens[0].type)&&"tokens"in a.tokens[0]&&a.tokens[0].tokens?(a.tokens[0].raw=x.raw+a.tokens[0].raw,a.tokens[0].text=x.raw+a.tokens[0].text,a.tokens[0].tokens.unshift(x)):a.tokens.unshift({type:"paragraph",raw:x.raw,text:x.raw,tokens:[x]}):a.tokens.unshift(x)}}else a.task&&(a.task=!1);if(!s.loose){let u=a.tokens.filter(d=>d.type==="space"),x=u.length>0&&u.some(d=>this.rules.other.anyLine.test(d.raw));s.loose=x}}if(s.loose)for(let a of s.items){a.loose=!0;for(let f of a.tokens)f.type==="text"&&(f.type="paragraph")}return s}}html(n){let e=this.rules.block.html.exec(n);if(e){let r=Zn(e[0]);return{type:"html",block:!0,raw:r,pre:e[1]==="pre"||e[1]==="script"||e[1]==="style",text:r}}}def(n){let e=this.rules.block.def.exec(n);if(e){let r=e[1].toLowerCase().replace(this.rules.other.multipleSpaceGlobal," "),t=e[2]?e[2].replace(this.rules.other.hrefBrackets,"$1").replace(this.rules.inline.anyPunctuation,"$1"):"",s=e[3]?e[3].substring(1,e[3].length-1).replace(this.rules.inline.anyPunctuation,"$1"):e[3];return{type:"def",tag:r,raw:Ee(e[0],`
`),href:t,title:s}}}table(n){var l;let e=this.rules.block.table.exec(n);if(!e||!this.rules.other.tableDelimiter.test(e[2]))return;let r=Vn(e[1]),t=e[2].replace(this.rules.other.tableAlignChars,"").split("|"),s=(l=e[3])!=null&&l.trim()?e[3].replace(this.rules.other.tableRowBlankLine,"").split(`
`):[],o={type:"table",raw:Ee(e[0],`
`),header:[],align:[],rows:[]};if(r.length===t.length){for(let c of t)this.rules.other.tableAlignRight.test(c)?o.align.push("right"):this.rules.other.tableAlignCenter.test(c)?o.align.push("center"):this.rules.other.tableAlignLeft.test(c)?o.align.push("left"):o.align.push(null);for(let c=0;c<r.length;c++)o.header.push({text:r[c],tokens:this.lexer.inline(r[c]),header:!0,align:o.align[c]});for(let c of s)o.rows.push(Vn(c,o.header.length).map((a,f)=>({text:a,tokens:this.lexer.inline(a),header:!1,align:o.align[f]})));return o}}lheading(n){let e=this.rules.block.lheading.exec(n);if(e){let r=e[1].trim();return{type:"heading",raw:Ee(e[0],`
`),depth:e[2].charAt(0)==="="?1:2,text:r,tokens:this.lexer.inline(r)}}}paragraph(n){let e=this.rules.block.paragraph.exec(n);if(e){let r=e[1].charAt(e[1].length-1)===`
`?e[1].slice(0,-1):e[1];return{type:"paragraph",raw:e[0],text:r,tokens:this.lexer.inline(r)}}}text(n){let e=this.rules.block.text.exec(n);if(e)return{type:"text",raw:e[0],text:e[0],tokens:this.lexer.inline(e[0])}}escape(n){let e=this.rules.inline.escape.exec(n);if(e)return{type:"escape",raw:e[0],text:e[1]}}tag(n){let e=this.rules.inline.tag.exec(n);if(e)return!this.lexer.state.inLink&&this.rules.other.startATag.test(e[0])?this.lexer.state.inLink=!0:this.lexer.state.inLink&&this.rules.other.endATag.test(e[0])&&(this.lexer.state.inLink=!1),!this.lexer.state.inRawBlock&&this.rules.other.startPreScriptTag.test(e[0])?this.lexer.state.inRawBlock=!0:this.lexer.state.inRawBlock&&this.rules.other.endPreScriptTag.test(e[0])&&(this.lexer.state.inRawBlock=!1),{type:"html",raw:e[0],inLink:this.lexer.state.inLink,inRawBlock:this.lexer.state.inRawBlock,block:!1,text:e[0]}}link(n){let e=this.rules.inline.link.exec(n);if(e){let r=e[2].trim();if(!this.options.pedantic&&this.rules.other.startAngleBracket.test(r)){if(!this.rules.other.endAngleBracket.test(r))return;let o=Ee(r.slice(0,-1),"\\");if((r.length-o.length)%2===0)return}else{let o=_s(e[2],"()");if(o===-2)return;if(o>-1){let l=(e[0].indexOf("!")===0?5:4)+e[1].length+o;e[2]=e[2].substring(0,o),e[0]=e[0].substring(0,l).trim(),e[3]=""}}let t=e[2],s="";if(this.options.pedantic){let o=this.rules.other.pedanticHrefTitle.exec(t);o&&(t=o[1],s=o[3])}else s=e[3]?e[3].slice(1,-1):"";return t=t.trim(),this.rules.other.startAngleBracket.test(t)&&(this.options.pedantic&&!this.rules.other.endAngleBracket.test(r)?t=t.slice(1):t=t.slice(1,-1)),Xn(e,{href:t&&t.replace(this.rules.inline.anyPunctuation,"$1"),title:s&&s.replace(this.rules.inline.anyPunctuation,"$1")},e[0],this.lexer,this.rules)}}reflink(n,e){let r;if((r=this.rules.inline.reflink.exec(n))||(r=this.rules.inline.nolink.exec(n))){let t=(r[2]||r[1]).replace(this.rules.other.multipleSpaceGlobal," "),s=e[t.toLowerCase()];if(!s){let o=r[0].charAt(0);return{type:"text",raw:o,text:o}}return Xn(r,s,r[0],this.lexer,this.rules)}}emStrong(n,e,r=""){let t=this.rules.inline.emStrongLDelim.exec(n);if(!(!t||!t[1]&&!t[2]&&!t[3]&&!t[4]||t[4]&&r.match(this.rules.other.unicodeAlphaNumeric))&&(!(t[1]||t[3])||!r||this.rules.inline.punctuation.exec(r))){let s=[...t[0]].length-1,o,l,c=s,a=0,f=t[0][0],u=r===f,x=f==="*"?this.rules.inline.emStrongRDelimAst:this.rules.inline.emStrongRDelimUnd;for(x.lastIndex=0,e=e.slice(-1*n.length+s);(t=x.exec(e))!==null;){if(o=t[1]||t[2]||t[3]||t[4]||t[5]||t[6],!o)continue;if(l=[...o].length,t[3]||t[4]){c+=l;continue}else if(t[5]||t[6]){if(s%3&&!((s+l)%3)){a+=l;continue}if(u)break}if(c-=l,c>0)continue;l=Math.min(l,l+c+a);let d=[...t[0]][0].length,v=n.slice(0,s+t.index+d+l);if(Math.min(s,l)%2){let A=v.slice(1,-1);return{type:"em",raw:v,text:A,tokens:this.lexer.inlineTokens(A)}}let m=v.slice(2,-2);return{type:"strong",raw:v,text:m,tokens:this.lexer.inlineTokens(m)}}}}codespan(n){let e=this.rules.inline.code.exec(n);if(e){let r=e[2].replace(this.rules.other.newLineCharGlobal," "),t=this.rules.other.nonSpaceChar.test(r),s=this.rules.other.startingSpaceChar.test(r)&&this.rules.other.endingSpaceChar.test(r);return t&&s&&(r=r.substring(1,r.length-1)),{type:"codespan",raw:e[0],text:r}}}br(n){let e=this.rules.inline.br.exec(n);if(e)return{type:"br",raw:e[0]}}del(n,e,r=""){let t=this.rules.inline.delLDelim.exec(n);if(t&&(!t[1]||!r||this.rules.inline.punctuation.exec(r))){let s=[...t[0]].length-1,o,l,c=s,a=this.rules.inline.delRDelim;for(a.lastIndex=0,e=e.slice(-1*n.length+s);(t=a.exec(e))!==null;){if(o=t[1]||t[2]||t[3]||t[4]||t[5]||t[6],!o||(l=[...o].length,l!==s))continue;if(t[3]||t[4]){c+=l;continue}if(c-=l,c>0)continue;l=Math.min(l,l+c);let f=[...t[0]][0].length,u=n.slice(0,s+t.index+f+l),x=u.slice(s,-s);return{type:"del",raw:u,text:x,tokens:this.lexer.inlineTokens(x)}}}}autolink(n){let e=this.rules.inline.autolink.exec(n);if(e){let r,t;return e[2]==="@"?(r=e[1],t="mailto:"+r):(r=e[1],t=r),{type:"link",raw:e[0],text:r,href:t,tokens:[{type:"text",raw:r,text:r}]}}}url(n){var r;let e;if(e=this.rules.inline.url.exec(n)){let t,s;if(e[2]==="@")t=e[0],s="mailto:"+t;else{let o;do o=e[0],e[0]=((r=this.rules.inline._backpedal.exec(e[0]))==null?void 0:r[0])??"";while(o!==e[0]);t=e[0],e[1]==="www."?s="http://"+e[0]:s=e[0]}return{type:"link",raw:e[0],text:t,href:s,tokens:[{type:"text",raw:t,text:t}]}}}inlineText(n){let e=this.rules.inline.text.exec(n);if(e){let r=this.lexer.state.inRawBlock;return{type:"text",raw:e[0],text:e[0],escaped:r}}}},ce=class _n{constructor(e){D(this,"tokens");D(this,"options");D(this,"state");D(this,"inlineQueue");D(this,"tokenizer");this.tokens=[],this.tokens.links=Object.create(null),this.options=e||Pe,this.options.tokenizer=this.options.tokenizer||new en,this.tokenizer=this.options.tokenizer,this.tokenizer.options=this.options,this.tokenizer.lexer=this,this.inlineQueue=[],this.state={inLink:!1,inRawBlock:!1,top:!0};let r={other:ee,block:Qe.normal,inline:Le.normal};this.options.pedantic?(r.block=Qe.pedantic,r.inline=Le.pedantic):this.options.gfm&&(r.block=Qe.gfm,this.options.breaks?r.inline=Le.breaks:r.inline=Le.gfm),this.tokenizer.rules=r}static get rules(){return{block:Qe,inline:Le}}static lex(e,r){return new _n(r).lex(e)}static lexInline(e,r){return new _n(r).inlineTokens(e)}lex(e){e=e.replace(ee.carriageReturn,`
`),this.blockTokens(e,this.tokens);for(let r=0;r<this.inlineQueue.length;r++){let t=this.inlineQueue[r];this.inlineTokens(t.src,t.tokens)}return this.inlineQueue=[],this.tokens}blockTokens(e,r=[],t=!1){var o,l,c;this.tokenizer.lexer=this,this.options.pedantic&&(e=e.replace(ee.tabCharGlobal,"    ").replace(ee.spaceLine,""));let s=1/0;for(;e;){if(e.length<s)s=e.length;else{this.infiniteLoopError(e.charCodeAt(0));break}let a;if((l=(o=this.options.extensions)==null?void 0:o.block)!=null&&l.some(u=>(a=u.call({lexer:this},e,r))?(e=e.substring(a.raw.length),r.push(a),!0):!1))continue;if(a=this.tokenizer.space(e)){e=e.substring(a.raw.length);let u=r.at(-1);a.raw.length===1&&u!==void 0?u.raw+=`
`:r.push(a);continue}if(a=this.tokenizer.code(e)){e=e.substring(a.raw.length);let u=r.at(-1);(u==null?void 0:u.type)==="paragraph"||(u==null?void 0:u.type)==="text"?(u.raw+=(u.raw.endsWith(`
`)?"":`
`)+a.raw,u.text+=`
`+a.text,this.inlineQueue.at(-1).src=u.text):r.push(a);continue}if(a=this.tokenizer.fences(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.heading(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.hr(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.blockquote(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.list(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.html(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.def(e)){e=e.substring(a.raw.length);let u=r.at(-1);(u==null?void 0:u.type)==="paragraph"||(u==null?void 0:u.type)==="text"?(u.raw+=(u.raw.endsWith(`
`)?"":`
`)+a.raw,u.text+=`
`+a.raw,this.inlineQueue.at(-1).src=u.text):this.tokens.links[a.tag]||(this.tokens.links[a.tag]={href:a.href,title:a.title},r.push(a));continue}if(a=this.tokenizer.table(e)){e=e.substring(a.raw.length),r.push(a);continue}if(a=this.tokenizer.lheading(e)){e=e.substring(a.raw.length),r.push(a);continue}let f=e;if((c=this.options.extensions)!=null&&c.startBlock){let u=1/0,x=e.slice(1),d;this.options.extensions.startBlock.forEach(v=>{d=v.call({lexer:this},x),typeof d=="number"&&d>=0&&(u=Math.min(u,d))}),u<1/0&&u>=0&&(f=e.substring(0,u+1))}if(this.state.top&&(a=this.tokenizer.paragraph(f))){let u=r.at(-1);t&&(u==null?void 0:u.type)==="paragraph"?(u.raw+=(u.raw.endsWith(`
`)?"":`
`)+a.raw,u.text+=`
`+a.text,this.inlineQueue.pop(),this.inlineQueue.at(-1).src=u.text):r.push(a),t=f.length!==e.length,e=e.substring(a.raw.length);continue}if(a=this.tokenizer.text(e)){e=e.substring(a.raw.length);let u=r.at(-1);(u==null?void 0:u.type)==="text"?(u.raw+=(u.raw.endsWith(`
`)?"":`
`)+a.raw,u.text+=`
`+a.text,this.inlineQueue.pop(),this.inlineQueue.at(-1).src=u.text):r.push(a);continue}if(e){this.infiniteLoopError(e.charCodeAt(0));break}}return this.state.top=!0,r}inline(e,r=[]){return this.inlineQueue.push({src:e,tokens:r}),r}inlineTokens(e,r=[]){var c,a,f,u,x;this.tokenizer.lexer=this;let t=e;if(this.tokens.links){let d=Object.keys(this.tokens.links);d.length>0&&(t=t.replace(this.tokenizer.rules.inline.reflinkSearch,v=>d.includes(v.slice(v.lastIndexOf("[")+1,-1))?"["+"a".repeat(v.length-2)+"]":v))}t=t.replace(this.tokenizer.rules.inline.anyPunctuation,"++"),t=t.replace(this.tokenizer.rules.inline.blockSkip,(d,v,m)=>{let A=m?m.length:0;return d.slice(0,A)+"["+"a".repeat(d.length-A-2)+"]"}),t=((a=(c=this.options.hooks)==null?void 0:c.emStrongMask)==null?void 0:a.call({lexer:this},t))??t;let s=!1,o="",l=1/0;for(;e;){if(e.length<l)l=e.length;else{this.infiniteLoopError(e.charCodeAt(0));break}s||(o=""),s=!1;let d;if((u=(f=this.options.extensions)==null?void 0:f.inline)!=null&&u.some(m=>(d=m.call({lexer:this},e,r))?(e=e.substring(d.raw.length),r.push(d),!0):!1))continue;if(d=this.tokenizer.escape(e)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.tag(e)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.link(e)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.reflink(e,this.tokens.links)){e=e.substring(d.raw.length);let m=r.at(-1);d.type==="text"&&(m==null?void 0:m.type)==="text"?(m.raw+=d.raw,m.text+=d.text):r.push(d);continue}if(d=this.tokenizer.emStrong(e,t,o)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.codespan(e)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.br(e)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.del(e,t,o)){e=e.substring(d.raw.length),r.push(d);continue}if(d=this.tokenizer.autolink(e)){e=e.substring(d.raw.length),r.push(d);continue}if(!this.state.inLink&&(d=this.tokenizer.url(e))){e=e.substring(d.raw.length),r.push(d);continue}let v=e;if((x=this.options.extensions)!=null&&x.startInline){let m=1/0,A=e.slice(1),j;this.options.extensions.startInline.forEach(G=>{j=G.call({lexer:this},A),typeof j=="number"&&j>=0&&(m=Math.min(m,j))}),m<1/0&&m>=0&&(v=e.substring(0,m+1))}if(d=this.tokenizer.inlineText(v)){e=e.substring(d.raw.length),d.raw.slice(-1)!=="_"&&(o=d.raw.slice(-1)),s=!0;let m=r.at(-1);(m==null?void 0:m.type)==="text"?(m.raw+=d.raw,m.text+=d.text):r.push(d);continue}if(e){this.infiniteLoopError(e.charCodeAt(0));break}}return r}infiniteLoopError(e){let r="Infinite loop on byte: "+e;if(this.options.silent)console.error(r);else throw new Error(r)}},nn=class{constructor(n){D(this,"options");D(this,"parser");this.options=n||Pe}space(n){return""}code({text:n,lang:e,escaped:r}){var o;let t=(o=(e||"").match(ee.notSpaceStart))==null?void 0:o[0],s=n.replace(ee.endingNewline,"")+`
`;return t?'<pre><code class="language-'+he(t)+'">'+(r?s:he(s,!0))+`</code></pre>
`:"<pre><code>"+(r?s:he(s,!0))+`</code></pre>
`}blockquote({tokens:n}){return`<blockquote>
${this.parser.parse(n)}</blockquote>
`}html({text:n}){return n}def(n){return""}heading({tokens:n,depth:e}){return`<h${e}>${this.parser.parseInline(n)}</h${e}>
`}hr(n){return`<hr>
`}list(n){let e=n.ordered,r=n.start,t="";for(let l=0;l<n.items.length;l++){let c=n.items[l];t+=this.listitem(c)}let s=e?"ol":"ul",o=e&&r!==1?' start="'+r+'"':"";return"<"+s+o+`>
`+t+"</"+s+`>
`}listitem(n){return`<li>${this.parser.parse(n.tokens)}</li>
`}checkbox({checked:n}){return"<input "+(n?'checked="" ':"")+'disabled="" type="checkbox"> '}paragraph({tokens:n}){return`<p>${this.parser.parseInline(n)}</p>
`}table(n){let e="",r="";for(let s=0;s<n.header.length;s++)r+=this.tablecell(n.header[s]);e+=this.tablerow({text:r});let t="";for(let s=0;s<n.rows.length;s++){let o=n.rows[s];r="";for(let l=0;l<o.length;l++)r+=this.tablecell(o[l]);t+=this.tablerow({text:r})}return t&&(t=`<tbody>${t}</tbody>`),`<table>
<thead>
`+e+`</thead>
`+t+`</table>
`}tablerow({text:n}){return`<tr>
${n}</tr>
`}tablecell(n){let e=this.parser.parseInline(n.tokens),r=n.header?"th":"td";return(n.align?`<${r} align="${n.align}">`:`<${r}>`)+e+`</${r}>
`}strong({tokens:n}){return`<strong>${this.parser.parseInline(n)}</strong>`}em({tokens:n}){return`<em>${this.parser.parseInline(n)}</em>`}codespan({text:n}){return`<code>${he(n,!0)}</code>`}br(n){return"<br>"}del({tokens:n}){return`<del>${this.parser.parseInline(n)}</del>`}link({href:n,title:e,tokens:r}){let t=this.parser.parseInline(r),s=Kn(n);if(s===null)return t;n=s;let o='<a href="'+n+'"';return e&&(o+=' title="'+he(e)+'"'),o+=">"+t+"</a>",o}image({href:n,title:e,text:r,tokens:t}){t&&(r=this.parser.parseInline(t,this.parser.textRenderer));let s=Kn(n);if(s===null)return he(r);n=s;let o=`<img src="${n}" alt="${he(r)}"`;return e&&(o+=` title="${he(e)}"`),o+=">",o}text(n){return"tokens"in n&&n.tokens?this.parser.parseInline(n.tokens):"escaped"in n&&n.escaped?n.text:he(n.text)}},Sn=class{strong({text:n}){return n}em({text:n}){return n}codespan({text:n}){return n}del({text:n}){return n}html({text:n}){return n}text({text:n}){return n}link({text:n}){return""+n}image({text:n}){return""+n}br(){return""}checkbox({raw:n}){return n}},pe=class mn{constructor(e){D(this,"options");D(this,"renderer");D(this,"textRenderer");this.options=e||Pe,this.options.renderer=this.options.renderer||new nn,this.renderer=this.options.renderer,this.renderer.options=this.options,this.renderer.parser=this,this.textRenderer=new Sn}static parse(e,r){return new mn(r).parse(e)}static parseInline(e,r){return new mn(r).parseInline(e)}parse(e){var t,s;this.renderer.parser=this;let r="";for(let o=0;o<e.length;o++){let l=e[o];if((s=(t=this.options.extensions)==null?void 0:t.renderers)!=null&&s[l.type]){let a=l,f=this.options.extensions.renderers[a.type].call({parser:this},a);if(f!==!1||!["space","hr","heading","code","table","blockquote","list","checkbox","html","def","paragraph","text"].includes(a.type)){r+=f||"";continue}}let c=l;switch(c.type){case"space":{r+=this.renderer.space(c);break}case"hr":{r+=this.renderer.hr(c);break}case"heading":{r+=this.renderer.heading(c);break}case"code":{r+=this.renderer.code(c);break}case"table":{r+=this.renderer.table(c);break}case"blockquote":{r+=this.renderer.blockquote(c);break}case"list":{r+=this.renderer.list(c);break}case"checkbox":{r+=this.renderer.checkbox(c);break}case"html":{r+=this.renderer.html(c);break}case"def":{r+=this.renderer.def(c);break}case"paragraph":{r+=this.renderer.paragraph(c);break}case"text":{r+=this.renderer.text(c);break}default:{let a='Token with "'+c.type+'" type was not found.';if(this.options.silent)return console.error(a),"";throw new Error(a)}}}return r}parseInline(e,r=this.renderer){var s,o;this.renderer.parser=this;let t="";for(let l=0;l<e.length;l++){let c=e[l];if((o=(s=this.options.extensions)==null?void 0:s.renderers)!=null&&o[c.type]){let f=this.options.extensions.renderers[c.type].call({parser:this},c);if(f!==!1||!["escape","html","link","image","checkbox","strong","em","codespan","br","del","text"].includes(c.type)){t+=f||"";continue}}let a=c;switch(a.type){case"escape":{t+=r.text(a);break}case"html":{t+=r.html(a);break}case"link":{t+=r.link(a);break}case"image":{t+=r.image(a);break}case"checkbox":{t+=r.checkbox(a);break}case"strong":{t+=r.strong(a);break}case"em":{t+=r.em(a);break}case"codespan":{t+=r.codespan(a);break}case"br":{t+=r.br(a);break}case"del":{t+=r.del(a);break}case"text":{t+=r.text(a);break}default:{let f='Token with "'+a.type+'" type was not found.';if(this.options.silent)return console.error(f),"";throw new Error(f)}}}return t}},Ye,De=(Ye=class{constructor(n){D(this,"options");D(this,"block");this.options=n||Pe}preprocess(n){return n}postprocess(n){return n}processAllTokens(n){return n}emStrongMask(n){return n}provideLexer(n=this.block){return n?ce.lex:ce.lexInline}provideParser(n=this.block){return n?pe.parse:pe.parseInline}},D(Ye,"passThroughHooks",new Set(["preprocess","postprocess","processAllTokens","emStrongMask"])),D(Ye,"passThroughHooksRespectAsync",new Set(["preprocess","postprocess","processAllTokens"])),Ye),ys=class{constructor(...n){D(this,"defaults",bn());D(this,"options",this.setOptions);D(this,"parse",this.parseMarkdown(!0));D(this,"parseInline",this.parseMarkdown(!1));D(this,"Parser",pe);D(this,"Renderer",nn);D(this,"TextRenderer",Sn);D(this,"Lexer",ce);D(this,"Tokenizer",en);D(this,"Hooks",De);this.use(...n)}walkTokens(n,e){var t,s;let r=[];for(let o of n)switch(r=r.concat(e.call(this,o)),o.type){case"table":{let l=o;for(let c of l.header)r=r.concat(this.walkTokens(c.tokens,e));for(let c of l.rows)for(let a of c)r=r.concat(this.walkTokens(a.tokens,e));break}case"list":{let l=o;r=r.concat(this.walkTokens(l.items,e));break}default:{let l=o;(s=(t=this.defaults.extensions)==null?void 0:t.childTokens)!=null&&s[l.type]?this.defaults.extensions.childTokens[l.type].forEach(c=>{let a=l[c].flat(1/0);r=r.concat(this.walkTokens(a,e))}):l.tokens&&(r=r.concat(this.walkTokens(l.tokens,e)))}}return r}use(...n){let e=this.defaults.extensions||{renderers:{},childTokens:{}};return n.forEach(r=>{let t={...r};if(t.async=this.defaults.async||t.async||!1,r.extensions&&(r.extensions.forEach(s=>{if(!s.name)throw new Error("extension name required");if("renderer"in s){let o=e.renderers[s.name];o?e.renderers[s.name]=function(...l){let c=s.renderer.apply(this,l);return c===!1&&(c=o.apply(this,l)),c}:e.renderers[s.name]=s.renderer}if("tokenizer"in s){if(!s.level||s.level!=="block"&&s.level!=="inline")throw new Error("extension level must be 'block' or 'inline'");let o=e[s.level];o?o.unshift(s.tokenizer):e[s.level]=[s.tokenizer],s.start&&(s.level==="block"?e.startBlock?e.startBlock.push(s.start):e.startBlock=[s.start]:s.level==="inline"&&(e.startInline?e.startInline.push(s.start):e.startInline=[s.start]))}"childTokens"in s&&s.childTokens&&(e.childTokens[s.name]=s.childTokens)}),t.extensions=e),r.renderer){let s=this.defaults.renderer||new nn(this.defaults);for(let o in r.renderer){if(!(o in s))throw new Error(`renderer '${o}' does not exist`);if(["options","parser"].includes(o))continue;let l=o,c=r.renderer[l],a=s[l];s[l]=(...f)=>{let u=c.apply(s,f);return u===!1&&(u=a.apply(s,f)),u||""}}t.renderer=s}if(r.tokenizer){let s=this.defaults.tokenizer||new en(this.defaults);for(let o in r.tokenizer){if(!(o in s))throw new Error(`tokenizer '${o}' does not exist`);if(["options","rules","lexer"].includes(o))continue;let l=o,c=r.tokenizer[l],a=s[l];s[l]=(...f)=>{let u=c.apply(s,f);return u===!1&&(u=a.apply(s,f)),u}}t.tokenizer=s}if(r.hooks){let s=this.defaults.hooks||new De;for(let o in r.hooks){if(!(o in s))throw new Error(`hook '${o}' does not exist`);if(["options","block"].includes(o))continue;let l=o,c=r.hooks[l],a=s[l];De.passThroughHooks.has(o)?s[l]=f=>{if(this.defaults.async&&De.passThroughHooksRespectAsync.has(o))return(async()=>{let x=await c.call(s,f);return a.call(s,x)})();let u=c.call(s,f);return a.call(s,u)}:s[l]=(...f)=>{if(this.defaults.async)return(async()=>{let x=await c.apply(s,f);return x===!1&&(x=await a.apply(s,f)),x})();let u=c.apply(s,f);return u===!1&&(u=a.apply(s,f)),u}}t.hooks=s}if(r.walkTokens){let s=this.defaults.walkTokens,o=r.walkTokens;t.walkTokens=function(l){let c=[];return c.push(o.call(this,l)),s&&(c=c.concat(s.call(this,l))),c}}this.defaults={...this.defaults,...t}}),this}setOptions(n){return this.defaults={...this.defaults,...n},this}lexer(n,e){return ce.lex(n,e??this.defaults)}parser(n,e){return pe.parse(n,e??this.defaults)}parseMarkdown(n){return(e,r)=>{let t={...r},s={...this.defaults,...t},o=this.onError(!!s.silent,!!s.async);if(this.defaults.async===!0&&t.async===!1)return o(new Error("marked(): The async option was set to true by an extension. Remove async: false from the parse options object to return a Promise."));if(typeof e>"u"||e===null)return o(new Error("marked(): input parameter is undefined or null"));if(typeof e!="string")return o(new Error("marked(): input parameter is of type "+Object.prototype.toString.call(e)+", string expected"));if(s.hooks&&(s.hooks.options=s,s.hooks.block=n),s.async)return(async()=>{let l=s.hooks?await s.hooks.preprocess(e):e,c=await(s.hooks?await s.hooks.provideLexer(n):n?ce.lex:ce.lexInline)(l,s),a=s.hooks?await s.hooks.processAllTokens(c):c;s.walkTokens&&await Promise.all(this.walkTokens(a,s.walkTokens));let f=await(s.hooks?await s.hooks.provideParser(n):n?pe.parse:pe.parseInline)(a,s);return s.hooks?await s.hooks.postprocess(f):f})().catch(o);try{s.hooks&&(e=s.hooks.preprocess(e));let l=(s.hooks?s.hooks.provideLexer(n):n?ce.lex:ce.lexInline)(e,s);s.hooks&&(l=s.hooks.processAllTokens(l)),s.walkTokens&&this.walkTokens(l,s.walkTokens);let c=(s.hooks?s.hooks.provideParser(n):n?pe.parse:pe.parseInline)(l,s);return s.hooks&&(c=s.hooks.postprocess(c)),c}catch(l){return o(l)}}}onError(n,e){return r=>{if(r.message+=`
Please report this to https://github.com/markedjs/marked.`,n){let t="<p>An error occurred:</p><pre>"+he(r.message+"",!0)+"</pre>";return e?Promise.resolve(t):t}if(e)return Promise.reject(r);throw r}}},Ae=new ys;function M(n,e){return Ae.parse(n,e)}M.options=M.setOptions=function(n){return Ae.setOptions(n),M.defaults=Ae.defaults,rr(M.defaults),M};M.getDefaults=bn;M.defaults=Pe;function xs(...n){return Ae.use(...n),M.defaults=Ae.defaults,rr(M.defaults),M}M.use=xs;M.walkTokens=function(n,e){return Ae.walkTokens(n,e)};M.parseInline=Ae.parseInline;M.Parser=pe;M.parser=pe.parse;M.Renderer=nn;M.TextRenderer=Sn;M.Lexer=ce;M.lexer=ce.lex;M.Tokenizer=en;M.Hooks=De;M.parse=M;M.options;M.setOptions;M.walkTokens;M.parseInline;pe.parse;ce.lex;var hn,Qn;function ks(){if(Qn)return hn;Qn=1;function n(i){return i instanceof Map?i.clear=i.delete=i.set=function(){throw new Error("map is read-only")}:i instanceof Set&&(i.add=i.clear=i.delete=function(){throw new Error("set is read-only")}),Object.freeze(i),Object.getOwnPropertyNames(i).forEach(p=>{const g=i[p],T=typeof g;(T==="object"||T==="function")&&!Object.isFrozen(g)&&n(g)}),i}class e{constructor(p){p.data===void 0&&(p.data={}),this.data=p.data,this.isMatchIgnored=!1}ignoreMatch(){this.isMatchIgnored=!0}}function r(i){return i.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;").replace(/'/g,"&#x27;")}function t(i,...p){const g=Object.create(null);for(const T in i)g[T]=i[T];return p.forEach(function(T){for(const W in T)g[W]=T[W]}),g}const s="</span>",o=i=>!!i.scope,l=(i,{prefix:p})=>{if(i.startsWith("language:"))return i.replace("language:","language-");if(i.includes(".")){const g=i.split(".");return[`${p}${g.shift()}`,...g.map((T,W)=>`${T}${"_".repeat(W+1)}`)].join(" ")}return`${p}${i}`};class c{constructor(p,g){this.buffer="",this.classPrefix=g.classPrefix,p.walk(this)}addText(p){this.buffer+=r(p)}openNode(p){if(!o(p))return;const g=l(p.scope,{prefix:this.classPrefix});this.span(g)}closeNode(p){o(p)&&(this.buffer+=s)}value(){return this.buffer}span(p){this.buffer+=`<span class="${p}">`}}const a=(i={})=>{const p={children:[]};return Object.assign(p,i),p};class f{constructor(){this.rootNode=a(),this.stack=[this.rootNode]}get top(){return this.stack[this.stack.length-1]}get root(){return this.rootNode}add(p){this.top.children.push(p)}openNode(p){const g=a({scope:p});this.add(g),this.stack.push(g)}closeNode(){if(this.stack.length>1)return this.stack.pop()}closeAllNodes(){for(;this.closeNode(););}toJSON(){return JSON.stringify(this.rootNode,null,4)}walk(p){return this.constructor._walk(p,this.rootNode)}static _walk(p,g){return typeof g=="string"?p.addText(g):g.children&&(p.openNode(g),g.children.forEach(T=>this._walk(p,T)),p.closeNode(g)),p}static _collapse(p){typeof p!="string"&&p.children&&(p.children.every(g=>typeof g=="string")?p.children=[p.children.join("")]:p.children.forEach(g=>{f._collapse(g)}))}}class u extends f{constructor(p){super(),this.options=p}addText(p){p!==""&&this.add(p)}startScope(p){this.openNode(p)}endScope(){this.closeNode()}__addSublanguage(p,g){const T=p.root;g&&(T.scope=`language:${g}`),this.add(T)}toHTML(){return new c(this,this.options).value()}finalize(){return this.closeAllNodes(),!0}}function x(i){return i?typeof i=="string"?i:i.source:null}function d(i){return A("(?=",i,")")}function v(i){return A("(?:",i,")*")}function m(i){return A("(?:",i,")?")}function A(...i){return i.map(g=>x(g)).join("")}function j(i){const p=i[i.length-1];return typeof p=="object"&&p.constructor===Object?(i.splice(i.length-1,1),p):{}}function G(...i){return"("+(j(i).capture?"":"?:")+i.map(T=>x(T)).join("|")+")"}function ne(i){return new RegExp(i.toString()+"|").exec("").length-1}function ue(i,p){const g=i&&i.exec(p);return g&&g.index===0}const P=/\[(?:[^\\\]]|\\.)*\]|\(\??|\\([1-9][0-9]*)|\\./;function b(i,{joinWith:p}){let g=0;return i.map(T=>{g+=1;const W=g;let K=x(T),k="";for(;K.length>0;){const y=P.exec(K);if(!y){k+=K;break}k+=K.substring(0,y.index),K=K.substring(y.index+y[0].length),y[0][0]==="\\"&&y[1]?k+="\\"+String(Number(y[1])+W):(k+=y[0],y[0]==="("&&g++)}return k}).map(T=>`(${T})`).join(p)}const C=/\b\B/,$="[a-zA-Z]\\w*",F="[a-zA-Z_]\\w*",q="\\b\\d+(\\.\\d+)?",Y="(-?)(\\b0[xX][a-fA-F0-9]+|(\\b\\d+(\\.\\d*)?|\\.\\d+)([eE][-+]?\\d+)?)",ye="\\b(0b[01]+)",H="!|!=|!==|%|%=|&|&&|&=|\\*|\\*=|\\+|\\+=|,|-|-=|/=|/|:|;|<<|<<=|<=|<|===|==|=|>>>=|>>=|>=|>>>|>>|>|\\?|\\[|\\{|\\(|\\^|\\^=|\\||\\|=|\\|\\||~",se=(i={})=>{const p=/^#![ ]*\//;return i.binary&&(i.begin=A(p,/.*\b/,i.binary,/\b.*/)),t({scope:"meta",begin:p,end:/$/,relevance:0,"on:begin":(g,T)=>{g.index!==0&&T.ignoreMatch()}},i)},ge={begin:"\\\\[\\s\\S]",relevance:0},ze={scope:"string",begin:"'",end:"'",illegal:"\\n",contains:[ge]},qe={scope:"string",begin:'"',end:'"',illegal:"\\n",contains:[ge]},dr={begin:/\b(a|an|the|are|I'm|isn't|don't|doesn't|won't|but|just|should|pretty|simply|enough|gonna|going|wtf|so|such|will|you|your|they|like|more)\b/},Fe=function(i,p,g={}){const T=t({scope:"comment",begin:i,end:p,contains:[]},g);T.contains.push({scope:"doctag",begin:"[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)",end:/(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):/,excludeBegin:!0,relevance:0});const W=G("I","a","is","so","us","to","at","if","in","it","on",/[A-Za-z]+['](d|ve|re|ll|t|s|n)/,/[A-Za-z]+[-][a-z]+/,/[A-Za-z][a-z]{2,}/);return T.contains.push({begin:A(/[ ]+/,"(",W,/[.]?[:]?([.][ ]|[ ])/,"){3}")}),T},fr=Fe("//","$"),hr=Fe("/\\*","\\*/"),gr=Fe("#","$"),_r={scope:"number",begin:q,relevance:0},mr={scope:"number",begin:Y,relevance:0},br={scope:"number",begin:ye,relevance:0},yr={scope:"regexp",begin:/\/(?=[^/\n]*\/)/,end:/\/[gimuy]*/,contains:[ge,{begin:/\[/,end:/\]/,relevance:0,contains:[ge]}]},xr={scope:"title",begin:$,relevance:0},kr={scope:"title",begin:F,relevance:0},wr={begin:"\\.\\s*"+F,relevance:0};var He=Object.freeze({__proto__:null,APOS_STRING_MODE:ze,BACKSLASH_ESCAPE:ge,BINARY_NUMBER_MODE:br,BINARY_NUMBER_RE:ye,COMMENT:Fe,C_BLOCK_COMMENT_MODE:hr,C_LINE_COMMENT_MODE:fr,C_NUMBER_MODE:mr,C_NUMBER_RE:Y,END_SAME_AS_BEGIN:function(i){return Object.assign(i,{"on:begin":(p,g)=>{g.data._beginMatch=p[1]},"on:end":(p,g)=>{g.data._beginMatch!==p[1]&&g.ignoreMatch()}})},HASH_COMMENT_MODE:gr,IDENT_RE:$,MATCH_NOTHING_RE:C,METHOD_GUARD:wr,NUMBER_MODE:_r,NUMBER_RE:q,PHRASAL_WORDS_MODE:dr,QUOTE_STRING_MODE:qe,REGEXP_MODE:yr,RE_STARTERS_RE:H,SHEBANG:se,TITLE_MODE:xr,UNDERSCORE_IDENT_RE:F,UNDERSCORE_TITLE_MODE:kr});function Er(i,p){i.input[i.index-1]==="."&&p.ignoreMatch()}function vr(i,p){i.className!==void 0&&(i.scope=i.className,delete i.className)}function Sr(i,p){p&&i.beginKeywords&&(i.begin="\\b("+i.beginKeywords.split(" ").join("|")+")(?!\\.)(?=\\b|\\s)",i.__beforeBegin=Er,i.keywords=i.keywords||i.beginKeywords,delete i.beginKeywords,i.relevance===void 0&&(i.relevance=0))}function Tr(i,p){Array.isArray(i.illegal)&&(i.illegal=G(...i.illegal))}function Rr(i,p){if(i.match){if(i.begin||i.end)throw new Error("begin & end are not supported with match");i.begin=i.match,delete i.match}}function Ar(i,p){i.relevance===void 0&&(i.relevance=1)}const Pr=(i,p)=>{if(!i.beforeMatch)return;if(i.starts)throw new Error("beforeMatch cannot be used with starts");const g=Object.assign({},i);Object.keys(i).forEach(T=>{delete i[T]}),i.keywords=g.keywords,i.begin=A(g.beforeMatch,d(g.begin)),i.starts={relevance:0,contains:[Object.assign(g,{endsParent:!0})]},i.relevance=0,delete g.beforeMatch},Cr=["of","and","for","in","not","or","if","then","parent","list","value"],Nr="keyword";function Tn(i,p,g=Nr){const T=Object.create(null);return typeof i=="string"?W(g,i.split(" ")):Array.isArray(i)?W(g,i):Object.keys(i).forEach(function(K){Object.assign(T,Tn(i[K],p,K))}),T;function W(K,k){p&&(k=k.map(y=>y.toLowerCase())),k.forEach(function(y){const S=y.split("|");T[S[0]]=[K,Or(S[0],S[1])]})}}function Or(i,p){return p?Number(p):Ir(i)?0:1}function Ir(i){return Cr.includes(i.toLowerCase())}const Rn={},ve=i=>{console.error(i)},An=(i,...p)=>{console.log(`WARN: ${i}`,...p)},Ce=(i,p)=>{Rn[`${i}/${p}`]||(console.log(`Deprecated as of ${i}. ${p}`),Rn[`${i}/${p}`]=!0)},je=new Error;function Pn(i,p,{key:g}){let T=0;const W=i[g],K={},k={};for(let y=1;y<=p.length;y++)k[y+T]=W[y],K[y+T]=!0,T+=ne(p[y-1]);i[g]=k,i[g]._emit=K,i[g]._multi=!0}function Mr(i){if(Array.isArray(i.begin)){if(i.skip||i.excludeBegin||i.returnBegin)throw ve("skip, excludeBegin, returnBegin not compatible with beginScope: {}"),je;if(typeof i.beginScope!="object"||i.beginScope===null)throw ve("beginScope must be object"),je;Pn(i,i.begin,{key:"beginScope"}),i.begin=b(i.begin,{joinWith:""})}}function Lr(i){if(Array.isArray(i.end)){if(i.skip||i.excludeEnd||i.returnEnd)throw ve("skip, excludeEnd, returnEnd not compatible with endScope: {}"),je;if(typeof i.endScope!="object"||i.endScope===null)throw ve("endScope must be object"),je;Pn(i,i.end,{key:"endScope"}),i.end=b(i.end,{joinWith:""})}}function Dr(i){i.scope&&typeof i.scope=="object"&&i.scope!==null&&(i.beginScope=i.scope,delete i.scope)}function $r(i){Dr(i),typeof i.beginScope=="string"&&(i.beginScope={_wrap:i.beginScope}),typeof i.endScope=="string"&&(i.endScope={_wrap:i.endScope}),Mr(i),Lr(i)}function Br(i){function p(k,y){return new RegExp(x(k),"m"+(i.case_insensitive?"i":"")+(i.unicodeRegex?"u":"")+(y?"g":""))}class g{constructor(){this.matchIndexes={},this.regexes=[],this.matchAt=1,this.position=0}addRule(y,S){S.position=this.position++,this.matchIndexes[this.matchAt]=S,this.regexes.push([S,y]),this.matchAt+=ne(y)+1}compile(){this.regexes.length===0&&(this.exec=()=>null);const y=this.regexes.map(S=>S[1]);this.matcherRe=p(b(y,{joinWith:"|"}),!0),this.lastIndex=0}exec(y){this.matcherRe.lastIndex=this.lastIndex;const S=this.matcherRe.exec(y);if(!S)return null;const X=S.findIndex((Me,sn)=>sn>0&&Me!==void 0),V=this.matchIndexes[X];return S.splice(0,X),Object.assign(S,V)}}class T{constructor(){this.rules=[],this.multiRegexes=[],this.count=0,this.lastIndex=0,this.regexIndex=0}getMatcher(y){if(this.multiRegexes[y])return this.multiRegexes[y];const S=new g;return this.rules.slice(y).forEach(([X,V])=>S.addRule(X,V)),S.compile(),this.multiRegexes[y]=S,S}resumingScanAtSamePosition(){return this.regexIndex!==0}considerAll(){this.regexIndex=0}addRule(y,S){this.rules.push([y,S]),S.type==="begin"&&this.count++}exec(y){const S=this.getMatcher(this.regexIndex);S.lastIndex=this.lastIndex;let X=S.exec(y);if(this.resumingScanAtSamePosition()&&!(X&&X.index===this.lastIndex)){const V=this.getMatcher(0);V.lastIndex=this.lastIndex+1,X=V.exec(y)}return X&&(this.regexIndex+=X.position+1,this.regexIndex===this.count&&this.considerAll()),X}}function W(k){const y=new T;return k.contains.forEach(S=>y.addRule(S.begin,{rule:S,type:"begin"})),k.terminatorEnd&&y.addRule(k.terminatorEnd,{type:"end"}),k.illegal&&y.addRule(k.illegal,{type:"illegal"}),y}function K(k,y){const S=k;if(k.isCompiled)return S;[vr,Rr,$r,Pr].forEach(V=>V(k,y)),i.compilerExtensions.forEach(V=>V(k,y)),k.__beforeBegin=null,[Sr,Tr,Ar].forEach(V=>V(k,y)),k.isCompiled=!0;let X=null;return typeof k.keywords=="object"&&k.keywords.$pattern&&(k.keywords=Object.assign({},k.keywords),X=k.keywords.$pattern,delete k.keywords.$pattern),X=X||/\w+/,k.keywords&&(k.keywords=Tn(k.keywords,i.case_insensitive)),S.keywordPatternRe=p(X,!0),y&&(k.begin||(k.begin=/\B|\b/),S.beginRe=p(S.begin),!k.end&&!k.endsWithParent&&(k.end=/\B|\b/),k.end&&(S.endRe=p(S.end)),S.terminatorEnd=x(S.end)||"",k.endsWithParent&&y.terminatorEnd&&(S.terminatorEnd+=(k.end?"|":"")+y.terminatorEnd)),k.illegal&&(S.illegalRe=p(k.illegal)),k.contains||(k.contains=[]),k.contains=[].concat(...k.contains.map(function(V){return zr(V==="self"?k:V)})),k.contains.forEach(function(V){K(V,S)}),k.starts&&K(k.starts,y),S.matcher=W(S),S}if(i.compilerExtensions||(i.compilerExtensions=[]),i.contains&&i.contains.includes("self"))throw new Error("ERR: contains `self` is not supported at the top-level of a language.  See documentation.");return i.classNameAliases=t(i.classNameAliases||{}),K(i)}function Cn(i){return i?i.endsWithParent||Cn(i.starts):!1}function zr(i){return i.variants&&!i.cachedVariants&&(i.cachedVariants=i.variants.map(function(p){return t(i,{variants:null},p)})),i.cachedVariants?i.cachedVariants:Cn(i)?t(i,{starts:i.starts?t(i.starts):null}):Object.isFrozen(i)?t(i):i}var qr="11.11.1";class Fr extends Error{constructor(p,g){super(p),this.name="HTMLInjectionError",this.html=g}}const tn=r,Nn=t,On=Symbol("nomatch"),Hr=7,In=function(i){const p=Object.create(null),g=Object.create(null),T=[];let W=!0;const K="Could not find the language '{}', did you forget to load/include a language module?",k={disableAutodetect:!0,name:"Plain text",contains:[]};let y={ignoreUnescapedHTML:!1,throwUnescapedHTML:!1,noHighlightRe:/^(no-?highlight)$/i,languageDetectRe:/\blang(?:uage)?-([\w-]+)\b/i,classPrefix:"hljs-",cssSelector:"pre code",languages:null,__emitter:u};function S(h){return y.noHighlightRe.test(h)}function X(h){let E=h.className+" ";E+=h.parentNode?h.parentNode.className:"";const I=y.languageDetectRe.exec(E);if(I){const B=xe(I[1]);return B||(An(K.replace("{}",I[1])),An("Falling back to no-highlight mode for this block.",h)),B?I[1]:"no-highlight"}return E.split(/\s+/).find(B=>S(B)||xe(B))}function V(h,E,I){let B="",Z="";typeof E=="object"?(B=h,I=E.ignoreIllegals,Z=E.language):(Ce("10.7.0","highlight(lang, code, ...args) has been deprecated."),Ce("10.7.0",`Please use highlight(code, options) instead.
https://github.com/highlightjs/highlight.js/issues/2277`),Z=h,B=E),I===void 0&&(I=!0);const ie={code:B,language:Z};Ge("before:highlight",ie);const ke=ie.result?ie.result:Me(ie.language,ie.code,I);return ke.code=ie.code,Ge("after:highlight",ke),ke}function Me(h,E,I,B){const Z=Object.create(null);function ie(_,w){return _.keywords[w]}function ke(){if(!R.keywords){Q.addText(z);return}let _=0;R.keywordPatternRe.lastIndex=0;let w=R.keywordPatternRe.exec(z),N="";for(;w;){N+=z.substring(_,w.index);const L=fe.case_insensitive?w[0].toLowerCase():w[0],J=ie(R,L);if(J){const[_e,it]=J;if(Q.addText(N),N="",Z[L]=(Z[L]||0)+1,Z[L]<=Hr&&(Ve+=it),_e.startsWith("_"))N+=w[0];else{const at=fe.classNameAliases[_e]||_e;de(w[0],at)}}else N+=w[0];_=R.keywordPatternRe.lastIndex,w=R.keywordPatternRe.exec(z)}N+=z.substring(_),Q.addText(N)}function We(){if(z==="")return;let _=null;if(typeof R.subLanguage=="string"){if(!p[R.subLanguage]){Q.addText(z);return}_=Me(R.subLanguage,z,!0,Fn[R.subLanguage]),Fn[R.subLanguage]=_._top}else _=an(z,R.subLanguage.length?R.subLanguage:null);R.relevance>0&&(Ve+=_.relevance),Q.__addSublanguage(_._emitter,_.language)}function re(){R.subLanguage!=null?We():ke(),z=""}function de(_,w){_!==""&&(Q.startScope(w),Q.addText(_),Q.endScope())}function $n(_,w){let N=1;const L=w.length-1;for(;N<=L;){if(!_._emit[N]){N++;continue}const J=fe.classNameAliases[_[N]]||_[N],_e=w[N];J?de(_e,J):(z=_e,ke(),z=""),N++}}function Bn(_,w){return _.scope&&typeof _.scope=="string"&&Q.openNode(fe.classNameAliases[_.scope]||_.scope),_.beginScope&&(_.beginScope._wrap?(de(z,fe.classNameAliases[_.beginScope._wrap]||_.beginScope._wrap),z=""):_.beginScope._multi&&($n(_.beginScope,w),z="")),R=Object.create(_,{parent:{value:R}}),R}function zn(_,w,N){let L=ue(_.endRe,N);if(L){if(_["on:end"]){const J=new e(_);_["on:end"](w,J),J.isMatchIgnored&&(L=!1)}if(L){for(;_.endsParent&&_.parent;)_=_.parent;return _}}if(_.endsWithParent)return zn(_.parent,w,N)}function et(_){return R.matcher.regexIndex===0?(z+=_[0],1):(pn=!0,0)}function nt(_){const w=_[0],N=_.rule,L=new e(N),J=[N.__beforeBegin,N["on:begin"]];for(const _e of J)if(_e&&(_e(_,L),L.isMatchIgnored))return et(w);return N.skip?z+=w:(N.excludeBegin&&(z+=w),re(),!N.returnBegin&&!N.excludeBegin&&(z=w)),Bn(N,_),N.returnBegin?0:w.length}function rt(_){const w=_[0],N=E.substring(_.index),L=zn(R,_,N);if(!L)return On;const J=R;R.endScope&&R.endScope._wrap?(re(),de(w,R.endScope._wrap)):R.endScope&&R.endScope._multi?(re(),$n(R.endScope,_)):J.skip?z+=w:(J.returnEnd||J.excludeEnd||(z+=w),re(),J.excludeEnd&&(z=w));do R.scope&&Q.closeNode(),!R.skip&&!R.subLanguage&&(Ve+=R.relevance),R=R.parent;while(R!==L.parent);return L.starts&&Bn(L.starts,_),J.returnEnd?0:w.length}function tt(){const _=[];for(let w=R;w!==fe;w=w.parent)w.scope&&_.unshift(w.scope);_.forEach(w=>Q.openNode(w))}let Ke={};function qn(_,w){const N=w&&w[0];if(z+=_,N==null)return re(),0;if(Ke.type==="begin"&&w.type==="end"&&Ke.index===w.index&&N===""){if(z+=E.slice(w.index,w.index+1),!W){const L=new Error(`0 width match regex (${h})`);throw L.languageName=h,L.badRule=Ke.rule,L}return 1}if(Ke=w,w.type==="begin")return nt(w);if(w.type==="illegal"&&!I){const L=new Error('Illegal lexeme "'+N+'" for mode "'+(R.scope||"<unnamed>")+'"');throw L.mode=R,L}else if(w.type==="end"){const L=rt(w);if(L!==On)return L}if(w.type==="illegal"&&N==="")return z+=`
`,1;if(cn>1e5&&cn>w.index*3)throw new Error("potential infinite loop, way more iterations than matches");return z+=N,N.length}const fe=xe(h);if(!fe)throw ve(K.replace("{}",h)),new Error('Unknown language: "'+h+'"');const st=Br(fe);let ln="",R=B||st;const Fn={},Q=new y.__emitter(y);tt();let z="",Ve=0,Se=0,cn=0,pn=!1;try{if(fe.__emitTokens)fe.__emitTokens(E,Q);else{for(R.matcher.considerAll();;){cn++,pn?pn=!1:R.matcher.considerAll(),R.matcher.lastIndex=Se;const _=R.matcher.exec(E);if(!_)break;const w=E.substring(Se,_.index),N=qn(w,_);Se=_.index+N}qn(E.substring(Se))}return Q.finalize(),ln=Q.toHTML(),{language:h,value:ln,relevance:Ve,illegal:!1,_emitter:Q,_top:R}}catch(_){if(_.message&&_.message.includes("Illegal"))return{language:h,value:tn(E),illegal:!0,relevance:0,_illegalBy:{message:_.message,index:Se,context:E.slice(Se-100,Se+100),mode:_.mode,resultSoFar:ln},_emitter:Q};if(W)return{language:h,value:tn(E),illegal:!1,relevance:0,errorRaised:_,_emitter:Q,_top:R};throw _}}function sn(h){const E={value:tn(h),illegal:!1,relevance:0,_top:k,_emitter:new y.__emitter(y)};return E._emitter.addText(h),E}function an(h,E){E=E||y.languages||Object.keys(p);const I=sn(h),B=E.filter(xe).filter(Dn).map(re=>Me(re,h,!1));B.unshift(I);const Z=B.sort((re,de)=>{if(re.relevance!==de.relevance)return de.relevance-re.relevance;if(re.language&&de.language){if(xe(re.language).supersetOf===de.language)return 1;if(xe(de.language).supersetOf===re.language)return-1}return 0}),[ie,ke]=Z,We=ie;return We.secondBest=ke,We}function jr(h,E,I){const B=E&&g[E]||I;h.classList.add("hljs"),h.classList.add(`language-${B}`)}function on(h){let E=null;const I=X(h);if(S(I))return;if(Ge("before:highlightElement",{el:h,language:I}),h.dataset.highlighted){console.log("Element previously highlighted. To highlight again, first unset `dataset.highlighted`.",h);return}if(h.children.length>0&&(y.ignoreUnescapedHTML||(console.warn("One of your code blocks includes unescaped HTML. This is a potentially serious security risk."),console.warn("https://github.com/highlightjs/highlight.js/wiki/security"),console.warn("The element with unescaped HTML:"),console.warn(h)),y.throwUnescapedHTML))throw new Fr("One of your code blocks includes unescaped HTML.",h.innerHTML);E=h;const B=E.textContent,Z=I?V(B,{language:I,ignoreIllegals:!0}):an(B);h.innerHTML=Z.value,h.dataset.highlighted="yes",jr(h,I,Z.language),h.result={language:Z.language,re:Z.relevance,relevance:Z.relevance},Z.secondBest&&(h.secondBest={language:Z.secondBest.language,relevance:Z.secondBest.relevance}),Ge("after:highlightElement",{el:h,result:Z,text:B})}function Ur(h){y=Nn(y,h)}const Gr=()=>{Ue(),Ce("10.6.0","initHighlighting() deprecated.  Use highlightAll() now.")};function Wr(){Ue(),Ce("10.6.0","initHighlightingOnLoad() deprecated.  Use highlightAll() now.")}let Mn=!1;function Ue(){function h(){Ue()}if(document.readyState==="loading"){Mn||window.addEventListener("DOMContentLoaded",h,!1),Mn=!0;return}document.querySelectorAll(y.cssSelector).forEach(on)}function Kr(h,E){let I=null;try{I=E(i)}catch(B){if(ve("Language definition for '{}' could not be registered.".replace("{}",h)),W)ve(B);else throw B;I=k}I.name||(I.name=h),p[h]=I,I.rawDefinition=E.bind(null,i),I.aliases&&Ln(I.aliases,{languageName:h})}function Vr(h){delete p[h];for(const E of Object.keys(g))g[E]===h&&delete g[E]}function Zr(){return Object.keys(p)}function xe(h){return h=(h||"").toLowerCase(),p[h]||p[g[h]]}function Ln(h,{languageName:E}){typeof h=="string"&&(h=[h]),h.forEach(I=>{g[I.toLowerCase()]=E})}function Dn(h){const E=xe(h);return E&&!E.disableAutodetect}function Xr(h){h["before:highlightBlock"]&&!h["before:highlightElement"]&&(h["before:highlightElement"]=E=>{h["before:highlightBlock"](Object.assign({block:E.el},E))}),h["after:highlightBlock"]&&!h["after:highlightElement"]&&(h["after:highlightElement"]=E=>{h["after:highlightBlock"](Object.assign({block:E.el},E))})}function Qr(h){Xr(h),T.push(h)}function Yr(h){const E=T.indexOf(h);E!==-1&&T.splice(E,1)}function Ge(h,E){const I=h;T.forEach(function(B){B[I]&&B[I](E)})}function Jr(h){return Ce("10.7.0","highlightBlock will be removed entirely in v12.0"),Ce("10.7.0","Please use highlightElement now."),on(h)}Object.assign(i,{highlight:V,highlightAuto:an,highlightAll:Ue,highlightElement:on,highlightBlock:Jr,configure:Ur,initHighlighting:Gr,initHighlightingOnLoad:Wr,registerLanguage:Kr,unregisterLanguage:Vr,listLanguages:Zr,getLanguage:xe,registerAliases:Ln,autoDetection:Dn,inherit:Nn,addPlugin:Qr,removePlugin:Yr}),i.debugMode=function(){W=!1},i.safeMode=function(){W=!0},i.versionString=qr,i.regex={concat:A,lookahead:d,either:G,optional:m,anyNumberOfTimes:v};for(const h in He)typeof He[h]=="object"&&n(He[h]);return Object.assign(i,He),i},Ne=In({});return Ne.newInstance=()=>In({}),hn=Ne,Ne.HighlightJS=Ne,Ne.default=Ne,hn}var ws=ks();const te=ct(ws);function Yn(n){const e=n.regex,r=new RegExp("[\\p{XID_Start}_]\\p{XID_Continue}*","u"),t=["and","as","assert","async","await","break","case","class","continue","def","del","elif","else","except","finally","for","from","global","if","import","in","is","lambda","match","nonlocal|10","not","or","pass","raise","return","try","while","with","yield"],c={$pattern:/[A-Za-z]\w+|__\w+__/,keyword:t,built_in:["__import__","abs","all","any","ascii","bin","bool","breakpoint","bytearray","bytes","callable","chr","classmethod","compile","complex","delattr","dict","dir","divmod","enumerate","eval","exec","filter","float","format","frozenset","getattr","globals","hasattr","hash","help","hex","id","input","int","isinstance","issubclass","iter","len","list","locals","map","max","memoryview","min","next","object","oct","open","ord","pow","print","property","range","repr","reversed","round","set","setattr","slice","sorted","staticmethod","str","sum","super","tuple","type","vars","zip"],literal:["__debug__","Ellipsis","False","None","NotImplemented","True"],type:["Any","Callable","Coroutine","Dict","List","Literal","Generic","Optional","Sequence","Set","Tuple","Type","Union"]},a={className:"meta",begin:/^(>>>|\.\.\.) /},f={className:"subst",begin:/\{/,end:/\}/,keywords:c,illegal:/#/},u={begin:/\{\{/,relevance:0},x={className:"string",contains:[n.BACKSLASH_ESCAPE],variants:[{begin:/([uU]|[bB]|[rR]|[bB][rR]|[rR][bB])?'''/,end:/'''/,contains:[n.BACKSLASH_ESCAPE,a],relevance:10},{begin:/([uU]|[bB]|[rR]|[bB][rR]|[rR][bB])?"""/,end:/"""/,contains:[n.BACKSLASH_ESCAPE,a],relevance:10},{begin:/([fF][rR]|[rR][fF]|[fF])'''/,end:/'''/,contains:[n.BACKSLASH_ESCAPE,a,u,f]},{begin:/([fF][rR]|[rR][fF]|[fF])"""/,end:/"""/,contains:[n.BACKSLASH_ESCAPE,a,u,f]},{begin:/([uU]|[rR])'/,end:/'/,relevance:10},{begin:/([uU]|[rR])"/,end:/"/,relevance:10},{begin:/([bB]|[bB][rR]|[rR][bB])'/,end:/'/},{begin:/([bB]|[bB][rR]|[rR][bB])"/,end:/"/},{begin:/([fF][rR]|[rR][fF]|[fF])'/,end:/'/,contains:[n.BACKSLASH_ESCAPE,u,f]},{begin:/([fF][rR]|[rR][fF]|[fF])"/,end:/"/,contains:[n.BACKSLASH_ESCAPE,u,f]},n.APOS_STRING_MODE,n.QUOTE_STRING_MODE]},d="[0-9](_?[0-9])*",v=`(\\b(${d}))?\\.(${d})|\\b(${d})\\.`,m=`\\b|${t.join("|")}`,A={className:"number",relevance:0,variants:[{begin:`(\\b(${d})|(${v}))[eE][+-]?(${d})[jJ]?(?=${m})`},{begin:`(${v})[jJ]?`},{begin:`\\b([1-9](_?[0-9])*|0+(_?0)*)[lLjJ]?(?=${m})`},{begin:`\\b0[bB](_?[01])+[lL]?(?=${m})`},{begin:`\\b0[oO](_?[0-7])+[lL]?(?=${m})`},{begin:`\\b0[xX](_?[0-9a-fA-F])+[lL]?(?=${m})`},{begin:`\\b(${d})[jJ](?=${m})`}]},j={className:"comment",begin:e.lookahead(/# type:/),end:/$/,keywords:c,contains:[{begin:/# type:/},{begin:/#/,end:/\b\B/,endsWithParent:!0}]},G={className:"params",variants:[{className:"",begin:/\(\s*\)/,skip:!0},{begin:/\(/,end:/\)/,excludeBegin:!0,excludeEnd:!0,keywords:c,contains:["self",a,A,x,n.HASH_COMMENT_MODE]}]};return f.contains=[x,A,a],{name:"Python",aliases:["py","gyp","ipython"],unicodeRegex:!0,keywords:c,illegal:/(<\/|\?)|=>/,contains:[a,A,{scope:"variable.language",match:/\bself\b/},{beginKeywords:"if",relevance:0},{match:/\bor\b/,scope:"keyword"},x,j,n.HASH_COMMENT_MODE,{match:[/\bdef/,/\s+/,r],scope:{1:"keyword",3:"title.function"},contains:[G]},{variants:[{match:[/\bclass/,/\s+/,r,/\s*/,/\(\s*/,r,/\s*\)/]},{match:[/\bclass/,/\s+/,r]}],scope:{1:"keyword",3:"title.class",6:"title.class.inherited"}},{className:"meta",begin:/^[\t ]*@/,end:/(?=#)|$/,contains:[A,G,x]}]}}function Jn(n){const e=n.regex,r={},t={begin:/\$\{/,end:/\}/,contains:["self",{begin:/:-/,contains:[r]}]};Object.assign(r,{className:"variable",variants:[{begin:e.concat(/\$[\w\d#@][\w\d_]*/,"(?![\\w\\d])(?![$])")},t]});const s={className:"subst",begin:/\$\(/,end:/\)/,contains:[n.BACKSLASH_ESCAPE]},o=n.inherit(n.COMMENT(),{match:[/(^|\s)/,/#.*$/],scope:{2:"comment"}}),l={begin:/<<-?\s*(?=\w+)/,starts:{contains:[n.END_SAME_AS_BEGIN({begin:/(\w+)/,end:/(\w+)/,className:"string"})]}},c={className:"string",begin:/"/,end:/"/,contains:[n.BACKSLASH_ESCAPE,r,s]};s.contains.push(c);const a={match:/\\"/},f={className:"string",begin:/'/,end:/'/},u={match:/\\'/},x={begin:/\$?\(\(/,end:/\)\)/,contains:[{begin:/\d+#[0-9a-f]+/,className:"number"},n.NUMBER_MODE,r]},d=["fish","bash","zsh","sh","csh","ksh","tcsh","dash","scsh"],v=n.SHEBANG({binary:`(${d.join("|")})`,relevance:10}),m={className:"function",begin:/\w[\w\d_]*\s*\(\s*\)\s*\{/,returnBegin:!0,contains:[n.inherit(n.TITLE_MODE,{begin:/\w[\w\d_]*/})],relevance:0},A=["if","then","else","elif","fi","time","for","while","until","in","do","done","case","esac","coproc","function","select"],j=["true","false"],G={match:/(\/[a-z._-]+)+/},ne=["break","cd","continue","eval","exec","exit","export","getopts","hash","pwd","readonly","return","shift","test","times","trap","umask","unset"],ue=["alias","bind","builtin","caller","command","declare","echo","enable","help","let","local","logout","mapfile","printf","read","readarray","source","sudo","type","typeset","ulimit","unalias"],P=["autoload","bg","bindkey","bye","cap","chdir","clone","comparguments","compcall","compctl","compdescribe","compfiles","compgroups","compquote","comptags","comptry","compvalues","dirs","disable","disown","echotc","echoti","emulate","fc","fg","float","functions","getcap","getln","history","integer","jobs","kill","limit","log","noglob","popd","print","pushd","pushln","rehash","sched","setcap","setopt","stat","suspend","ttyctl","unfunction","unhash","unlimit","unsetopt","vared","wait","whence","where","which","zcompile","zformat","zftp","zle","zmodload","zparseopts","zprof","zpty","zregexparse","zsocket","zstyle","ztcp"],b=["chcon","chgrp","chown","chmod","cp","dd","df","dir","dircolors","ln","ls","mkdir","mkfifo","mknod","mktemp","mv","realpath","rm","rmdir","shred","sync","touch","truncate","vdir","b2sum","base32","base64","cat","cksum","comm","csplit","cut","expand","fmt","fold","head","join","md5sum","nl","numfmt","od","paste","ptx","pr","sha1sum","sha224sum","sha256sum","sha384sum","sha512sum","shuf","sort","split","sum","tac","tail","tr","tsort","unexpand","uniq","wc","arch","basename","chroot","date","dirname","du","echo","env","expr","factor","groups","hostid","id","link","logname","nice","nohup","nproc","pathchk","pinky","printenv","printf","pwd","readlink","runcon","seq","sleep","stat","stdbuf","stty","tee","test","timeout","tty","uname","unlink","uptime","users","who","whoami","yes"];return{name:"Bash",aliases:["sh","zsh"],keywords:{$pattern:/\b[a-z][a-z0-9._-]+\b/,keyword:A,literal:j,built_in:[...ne,...ue,"set","shopt",...P,...b]},contains:[v,n.SHEBANG(),m,x,o,l,G,c,a,f,u,r]}}function Es(n){const e=n.regex,r=n.COMMENT("--","$"),t={scope:"string",variants:[{begin:/'/,end:/'/,contains:[{match:/''/}]}]},s={begin:/"/,end:/"/,contains:[{match:/""/}]},o=["true","false","unknown"],l=["double precision","large object","with timezone","without timezone"],c=["bigint","binary","blob","boolean","char","character","clob","date","dec","decfloat","decimal","float","int","integer","interval","nchar","nclob","national","numeric","real","row","smallint","time","timestamp","varchar","varying","varbinary"],a=["add","asc","collation","desc","final","first","last","view"],f=["abs","acos","all","allocate","alter","and","any","are","array","array_agg","array_max_cardinality","as","asensitive","asin","asymmetric","at","atan","atomic","authorization","avg","begin","begin_frame","begin_partition","between","bigint","binary","blob","boolean","both","by","call","called","cardinality","cascaded","case","cast","ceil","ceiling","char","char_length","character","character_length","check","classifier","clob","close","coalesce","collate","collect","column","commit","condition","connect","constraint","contains","convert","copy","corr","corresponding","cos","cosh","count","covar_pop","covar_samp","create","cross","cube","cume_dist","current","current_catalog","current_date","current_default_transform_group","current_path","current_role","current_row","current_schema","current_time","current_timestamp","current_path","current_role","current_transform_group_for_type","current_user","cursor","cycle","date","day","deallocate","dec","decimal","decfloat","declare","default","define","delete","dense_rank","deref","describe","deterministic","disconnect","distinct","double","drop","dynamic","each","element","else","empty","end","end_frame","end_partition","end-exec","equals","escape","every","except","exec","execute","exists","exp","external","extract","false","fetch","filter","first_value","float","floor","for","foreign","frame_row","free","from","full","function","fusion","get","global","grant","group","grouping","groups","having","hold","hour","identity","in","indicator","initial","inner","inout","insensitive","insert","int","integer","intersect","intersection","interval","into","is","join","json_array","json_arrayagg","json_exists","json_object","json_objectagg","json_query","json_table","json_table_primitive","json_value","lag","language","large","last_value","lateral","lead","leading","left","like","like_regex","listagg","ln","local","localtime","localtimestamp","log","log10","lower","match","match_number","match_recognize","matches","max","member","merge","method","min","minute","mod","modifies","module","month","multiset","national","natural","nchar","nclob","new","no","none","normalize","not","nth_value","ntile","null","nullif","numeric","octet_length","occurrences_regex","of","offset","old","omit","on","one","only","open","or","order","out","outer","over","overlaps","overlay","parameter","partition","pattern","per","percent","percent_rank","percentile_cont","percentile_disc","period","portion","position","position_regex","power","precedes","precision","prepare","primary","procedure","ptf","range","rank","reads","real","recursive","ref","references","referencing","regr_avgx","regr_avgy","regr_count","regr_intercept","regr_r2","regr_slope","regr_sxx","regr_sxy","regr_syy","release","result","return","returns","revoke","right","rollback","rollup","row","row_number","rows","running","savepoint","scope","scroll","search","second","seek","select","sensitive","session_user","set","show","similar","sin","sinh","skip","smallint","some","specific","specifictype","sql","sqlexception","sqlstate","sqlwarning","sqrt","start","static","stddev_pop","stddev_samp","submultiset","subset","substring","substring_regex","succeeds","sum","symmetric","system","system_time","system_user","table","tablesample","tan","tanh","then","time","timestamp","timezone_hour","timezone_minute","to","trailing","translate","translate_regex","translation","treat","trigger","trim","trim_array","true","truncate","uescape","union","unique","unknown","unnest","update","upper","user","using","value","values","value_of","var_pop","var_samp","varbinary","varchar","varying","versioning","when","whenever","where","width_bucket","window","with","within","without","year"],u=["abs","acos","array_agg","asin","atan","avg","cast","ceil","ceiling","coalesce","corr","cos","cosh","count","covar_pop","covar_samp","cume_dist","dense_rank","deref","element","exp","extract","first_value","floor","json_array","json_arrayagg","json_exists","json_object","json_objectagg","json_query","json_table","json_table_primitive","json_value","lag","last_value","lead","listagg","ln","log","log10","lower","max","min","mod","nth_value","ntile","nullif","percent_rank","percentile_cont","percentile_disc","position","position_regex","power","rank","regr_avgx","regr_avgy","regr_count","regr_intercept","regr_r2","regr_slope","regr_sxx","regr_sxy","regr_syy","row_number","sin","sinh","sqrt","stddev_pop","stddev_samp","substring","substring_regex","sum","tan","tanh","translate","translate_regex","treat","trim","trim_array","unnest","upper","value_of","var_pop","var_samp","width_bucket"],x=["current_catalog","current_date","current_default_transform_group","current_path","current_role","current_schema","current_transform_group_for_type","current_user","session_user","system_time","system_user","current_time","localtime","current_timestamp","localtimestamp"],d=["create table","insert into","primary key","foreign key","not null","alter table","add constraint","grouping sets","on overflow","character set","respect nulls","ignore nulls","nulls first","nulls last","depth first","breadth first"],v=u,m=[...f,...a].filter(b=>!u.includes(b)),A={scope:"variable",match:/@[a-z0-9][a-z0-9_]*/},j={scope:"operator",match:/[-+*/=%^~]|&&?|\|\|?|!=?|<(?:=>?|<|>)?|>[>=]?/,relevance:0},G={match:e.concat(/\b/,e.either(...v),/\s*\(/),relevance:0,keywords:{built_in:v}};function ne(b){return e.concat(/\b/,e.either(...b.map(C=>C.replace(/\s+/,"\\s+"))),/\b/)}const ue={scope:"keyword",match:ne(d),relevance:0};function P(b,{exceptions:C,when:$}={}){const F=$;return C=C||[],b.map(q=>q.match(/\|\d+$/)||C.includes(q)?q:F(q)?`${q}|0`:q)}return{name:"SQL",case_insensitive:!0,illegal:/[{}]|<\//,keywords:{$pattern:/\b[\w\.]+/,keyword:P(m,{when:b=>b.length<3}),literal:o,type:c,built_in:x},contains:[{scope:"type",match:ne(l)},ue,G,A,t,s,n.C_NUMBER_MODE,n.C_BLOCK_COMMENT_MODE,r,j]}}function er(n){return{name:"Dockerfile",aliases:["docker"],case_insensitive:!0,keywords:["from","maintainer","expose","env","arg","user","onbuild","stopsignal"],contains:[n.HASH_COMMENT_MODE,n.APOS_STRING_MODE,n.QUOTE_STRING_MODE,n.NUMBER_MODE,{beginKeywords:"run cmd entrypoint volume add copy workdir label healthcheck shell",starts:{end:/[^\\]$/,subLanguage:"bash"}}],illegal:"</"}}function vs(n){const e="true false yes no null",r="[\\w#;/?:@&=+$,.~*'()[\\]]+",t={className:"attr",variants:[{begin:/[\w*@][\w*@ :()\./-]*:(?=[ \t]|$)/},{begin:/"[\w*@][\w*@ :()\./-]*":(?=[ \t]|$)/},{begin:/'[\w*@][\w*@ :()\./-]*':(?=[ \t]|$)/}]},s={className:"template-variable",variants:[{begin:/\{\{/,end:/\}\}/},{begin:/%\{/,end:/\}/}]},o={className:"string",relevance:0,begin:/'/,end:/'/,contains:[{match:/''/,scope:"char.escape",relevance:0}]},l={className:"string",relevance:0,variants:[{begin:/"/,end:/"/},{begin:/\S+/}],contains:[n.BACKSLASH_ESCAPE,s]},c=n.inherit(l,{variants:[{begin:/'/,end:/'/,contains:[{begin:/''/,relevance:0}]},{begin:/"/,end:/"/},{begin:/[^\s,{}[\]]+/}]}),d={className:"number",begin:"\\b"+"[0-9]{4}(-[0-9][0-9]){0,2}"+"([Tt \\t][0-9][0-9]?(:[0-9][0-9]){2})?"+"(\\.[0-9]*)?"+"([ \\t])*(Z|[-+][0-9][0-9]?(:[0-9][0-9])?)?"+"\\b"},v={end:",",endsWithParent:!0,excludeEnd:!0,keywords:e,relevance:0},m={begin:/\{/,end:/\}/,contains:[v],illegal:"\\n",relevance:0},A={begin:"\\[",end:"\\]",contains:[v],illegal:"\\n",relevance:0},j=[t,{className:"meta",begin:"^---\\s*$",relevance:10},{className:"string",begin:"[\\|>]([1-9]?[+-])?[ ]*\\n( +)[^ ][^\\n]*\\n(\\2[^\\n]+\\n?)*"},{begin:"<%[%=-]?",end:"[%-]?%>",subLanguage:"ruby",excludeBegin:!0,excludeEnd:!0,relevance:0},{className:"type",begin:"!\\w+!"+r},{className:"type",begin:"!<"+r+">"},{className:"type",begin:"!"+r},{className:"type",begin:"!!"+r},{className:"meta",begin:"&"+n.UNDERSCORE_IDENT_RE+"$"},{className:"meta",begin:"\\*"+n.UNDERSCORE_IDENT_RE+"$"},{className:"bullet",begin:"-(?=[ ]|$)",relevance:0},n.HASH_COMMENT_MODE,{beginKeywords:e,keywords:{literal:e}},d,{className:"number",begin:n.C_NUMBER_RE+"\\b",relevance:0},m,A,o,l],G=[...j];return G.pop(),G.push(c),v.contains=G,{name:"YAML",case_insensitive:!0,aliases:["yml"],contains:j}}function nr(n){return{name:"Plain text",aliases:["text","txt"],disableAutodetect:!0}}const Ss=`# Python 知识库\r
\r
> 涵盖 Python 从入门到精通的核心知识体系，包含语法基础、进阶特性、主流框架与实战教程。\r
> 每个知识点都配有详细讲解、代码示例、常见陷阱和最佳实践。\r
\r
---\r
\r
## 目录\r
\r
- [一、Python 基础](#一python-基础)\r
- [二、数据结构](#二数据结构)\r
- [三、函数与模块](#三函数与模块)\r
- [四、面向对象编程](#四面向对象编程)\r
- [五、文件与 IO](#五文件与-io)\r
- [六、异常处理](#六异常处理)\r
- [七、进阶特性](#七进阶特性)\r
- [八、并发编程](#八并发编程)\r
- [九、标准库精选](#九标准库精选)\r
- [十、Web 开发](#十web-开发)\r
- [十一、数据科学与 AI](#十一数据科学与-ai)\r
- [十二、数据库操作](#十二数据库操作)\r
- [十三、测试与调试](#十三测试与调试)\r
- [十四、工程化实践](#十四工程化实践)\r
- [十五、学习资源与路线图](#十五学习资源与路线图)\r
\r
---\r
\r
## 一、Python 基础\r
\r
### 1.1 环境搭建\r
\r
Python 的环境管理是新手最容易踩坑的地方。核心原则：**每个项目使用独立的虚拟环境**，避免不同项目的依赖版本互相冲突。\r
\r
**安装 Python**\r
\r
推荐从官网下载最新稳定版（3.10+）。Windows 用户安装时务必勾选 **"Add Python to PATH"**，否则命令行无法直接使用 \`python\` 命令。\r
\r
\`\`\`bash\r
# 验证安装\r
python --version       # Python 3.12.x\r
\r
# Windows 可能有两个版本共存，用 py 启动器指定\r
py -3.10 --version\r
py -3.12 --version\r
\`\`\`\r
\r
**虚拟环境（必须掌握）**\r
\r
虚拟环境会为每个项目创建独立的 Python 解释器和包目录。这样项目 A 用 \`requests==2.28\`，项目 B 用 \`requests==2.31\`，互不干扰。\r
\r
\`\`\`bash\r
# 创建虚拟环境（在项目根目录下）\r
python -m venv .venv\r
\r
# 激活虚拟环境\r
.venv\\Scripts\\activate       # Windows (PowerShell)\r
# source .venv/bin/activate  # Linux/Mac\r
\r
# 激活后命令行提示符前面会出现 (.venv)\r
# 此时 pip install 的包都装在这个虚拟环境里\r
\r
# 退出虚拟环境\r
deactivate\r
\`\`\`\r
\r
> **常见陷阱**：VS Code 打开项目后需要选择 Python 解释器（Ctrl+Shift+P → Python: Select Interpreter → 选择 \`.venv\` 中的那个），否则编辑器内的代码提示和终端运行的可能不是同一个环境。\r
\r
**pip 包管理**\r
\r
\`\`\`bash\r
pip install requests                  # 安装最新稳定版\r
pip install requests==2.31.0          # 安装指定版本\r
pip install "requests>=2.28,<3.0"     # 版本范围\r
pip install -r requirements.txt       # 从文件批量安装\r
pip install --upgrade requests        # 升级\r
\r
pip freeze > requirements.txt         # 导出当前环境所有依赖及版本\r
pip list                              # 查看已安装的包\r
pip show requests                     # 查看某个包的详细信息\r
pip uninstall requests                # 卸载\r
\`\`\`\r
\r
> **最佳实践**：\`requirements.txt\` 里锁定版本号（\`requests==2.31.0\`），保证团队和部署环境一致。更现代的方案用 \`pyproject.toml\` + \`pip-tools\` 或 \`Poetry\`。\r
\r
**pyenv（多版本管理，推荐）**\r
\r
当项目需要不同版本的 Python 时，pyenv 可以方便地切换：\r
\r
\`\`\`bash\r
# 安装 pyenv（Windows 用 pyenv-win）\r
# https://github.com/pyenv-win/pyenv-win\r
\r
pyenv install 3.10.13     # 安装指定版本\r
pyenv install 3.12.1\r
pyenv versions             # 列出已安装版本\r
pyenv global 3.12.1        # 设置全局默认\r
pyenv local 3.10.13        # 在当前目录设置版本（会创建 .python-version 文件）\r
\`\`\`\r
\r
### 1.2 变量与数据类型\r
\r
Python 是**动态强类型**语言：变量不需要声明类型（动态），但不会隐式做不安全的类型转换（强类型）。\`"1" + 2\` 会报错，不会自动把字符串转成数字。\r
\r
**基本数据类型**\r
\r
\`\`\`python\r
# 整数 int —— Python 3 的 int 没有大小限制，支持任意精度\r
a = 42\r
big = 10 ** 100           # 大整数不会溢出\r
print(type(big))           # <class 'int'>\r
\r
# 浮点数 float —— 基于 C 的 double（64 位），有精度问题\r
0.1 + 0.2                # 0.30000000000000004（不是 0.3！）\r
# 需要精确计算时用 decimal 模块\r
from decimal import Decimal\r
Decimal("0.1") + Decimal("0.2")  # Decimal('0.3')\r
\r
# 布尔值 bool —— 是 int 的子类！\r
True + True              # 2\r
isinstance(True, int)    # True\r
\r
# None —— 表示"没有值"，单例对象\r
x = None\r
x is None                # 用 is 判断，不用 ==\r
\`\`\`\r
\r
> **浮点精度陷阱**：涉及金额计算时，永远不要用 float。用 \`Decimal\`（精确十进制）或存整数分（\`100\` 表示 1 元）。\r
\r
**字符串 str**\r
\r
字符串是**不可变序列**，任何修改操作都会创建新字符串。\r
\r
\`\`\`python\r
# 创建\r
s1 = "hello"\r
s2 = 'hello'              # 单双引号等价\r
s3 = """多行\r
字符串"""                   # 三引号保留换行\r
s4 = r"C:\\new\\test"       # raw 字符串，不转义（正则和路径常用）\r
\r
# 常用方法\r
"hello world".upper()              # "HELLO WORLD"\r
"  hello  ".strip()                # "hello"（去两端空白）\r
"hello".startswith("he")           # True\r
"hello".find("ll")                 # 2（找不到返回 -1）\r
"hello".replace("l", "L")          # "heLLo"\r
"a,b,c".split(",")                 # ["a", "b", "c"]\r
"-".join(["a", "b", "c"])          # "a-b-c"\r
"hello".center(20, "-")            # "-------hello--------"\r
\r
# 字符串切片（和 list 一样支持切片语法）\r
s = "Hello, World!"\r
s[0:5]      # "Hello"\r
s[-1]       # "!"\r
s[::-1]     # "!dlroW ,olleH"（反转）\r
\r
# 判断类方法\r
"123".isdigit()       # True\r
"abc".isalpha()       # True\r
"abc123".isalnum()    # True\r
\`\`\`\r
\r
**f-string 格式化（Python 3.6+，强烈推荐）**\r
\r
\`\`\`python\r
name = "Alice"\r
age = 25\r
price = 99.5\r
\r
# 基础用法\r
print(f"姓名：{name}，年龄：{age}")\r
\r
# 格式控制\r
print(f"价格：{price:.2f}")        # 99.50（保留 2 位小数）\r
print(f"百分比：{0.856:.1%}")      # 85.6%\r
print(f"补零：{42:06d}")           # 000042\r
print(f"左对齐：{'hi':<10}|")      # "hi        |"\r
print(f"右对齐：{'hi':>10}|")      # "        hi|"\r
print(f"居中：{'hi':^10}|")        # "    hi    |"\r
print(f"千分位：{1234567:,}")       # 1,234,567\r
\r
# 可以放表达式\r
print(f"{2 + 3 = }")               # "2 + 3 = 5"（Python 3.8+ 调试利器）\r
print(f"{name.upper() = }")        # "name.upper() = 'ALICE'"\r
\r
# 多行 f-string\r
msg = f"""\r
尊敬的 {name}：\r
  您的账户余额为 {price:.2f} 元。\r
"""\r
\`\`\`\r
\r
**类型转换与检查**\r
\r
\`\`\`python\r
# 类型转换\r
int("42")          # 42\r
float("3.14")      # 3.14\r
str(42)            # "42"\r
bool(0)            # False（0、空字符串、None、空容器都是 False）\r
bool("hello")      # True\r
list("abc")        # ['a', 'b', 'c']\r
tuple([1, 2, 3])   # (1, 2, 3)\r
\r
# 类型检查\r
type(42) == int              # True\r
isinstance(42, int)          # True（推荐，支持继承关系）\r
isinstance(True, int)        # True（bool 是 int 的子类）\r
type(42) is int              # True（严格比较，不考虑继承）\r
\`\`\`\r
\r
> **isinstance vs type**：优先用 \`isinstance\`，它考虑继承关系。\`type(True) is int\` 返回 \`False\`，但 \`isinstance(True, int)\` 返回 \`True\`。\r
\r
### 1.3 运算符\r
\r
\`\`\`python\r
# ===== 算术运算 =====\r
10 / 3       # 3.333...  真除法（结果永远是 float）\r
10 // 3      # 3         整除（向下取整，负数注意：-10 // 3 = -4）\r
10 % 3       # 1         取余（符号跟除数：-10 % 3 = 2）\r
2 ** 10      # 1024      幂运算\r
\r
# ===== 比较运算 =====\r
x == y       # 值相等（会调用 __eq__）\r
x != y       # 值不等\r
x is y       # 同一对象（id 相同，内存地址一样）\r
x is not y   # 不同对象\r
\r
# == vs is 的区别（面试高频题）\r
a = [1, 2, 3]\r
b = [1, 2, 3]\r
a == b       # True（值相等）\r
a is b       # False（不是同一个对象）\r
\r
# Python 对小整数（-5 ~ 256）和短字符串有缓存\r
x = 256\r
y = 256\r
x is y       # True（缓存）\r
\r
x = 257\r
y = 257\r
x is y       # False（超出缓存范围）\r
\r
# ===== 逻辑运算（短路求值） =====\r
True and False   # False（遇到第一个 False 就停止）\r
True or False    # True（遇到第一个 True 就停止）\r
not True         # False\r
\r
# 短路求值的实际应用\r
# 避免 NoneType 错误\r
user and user.name        # user 为 None 时不会报错\r
\r
# 给变量赋默认值\r
name = input_name or "默认名称"   # input_name 为空/None 时用默认值\r
\r
# ===== 成员运算 =====\r
"hello" in "hello world"    # True\r
3 in [1, 2, 3, 4]          # True\r
"key" in {"key": "value"}   # True（检查键是否存在）\r
\r
# ===== 海象运算符（Python 3.8+） =====\r
# 在表达式中同时赋值，减少重复计算\r
if (n := len(data)) > 10:\r
    print(f"数据量过大：{n} 条")\r
\r
# 在 while 循环中很实用\r
while (line := file.readline()).strip():\r
    process(line)\r
\`\`\`\r
\r
### 1.4 控制流\r
\r
\`\`\`python\r
# ===== if-elif-else =====\r
score = 85\r
if score >= 90:\r
    grade = "A"\r
elif score >= 80:\r
    grade = "B"\r
elif score >= 70:\r
    grade = "C"\r
else:\r
    grade = "D"\r
\r
# 三元表达式（条件写在中间）\r
status = "成年" if age >= 18 else "未成年"\r
# 等价于：\r
# if age >= 18:\r
#     status = "成年"\r
# else:\r
#     status = "未成年"\r
\r
# ===== for 循环 =====\r
# range 的三种用法\r
for i in range(5):          # 0, 1, 2, 3, 4\r
    print(i)\r
\r
for i in range(2, 8):       # 2, 3, 4, 5, 6, 7\r
    print(i)\r
\r
for i in range(0, 10, 2):   # 0, 2, 4, 6, 8（步长 2）\r
    print(i)\r
\r
# 带索引遍历（比 range(len()) 更 Pythonic）\r
fruits = ["apple", "banana", "cherry"]\r
for i, fruit in enumerate(fruits):\r
    print(f"{i}: {fruit}")\r
\r
# 同时遍历多个序列\r
names = ["Alice", "Bob"]\r
ages = [25, 30]\r
for name, age in zip(names, ages):\r
    print(f"{name} is {age}")\r
\r
# for-else（循环正常结束时执行 else，break 跳出则不执行）\r
for n in range(2, 10):\r
    for x in range(2, n):\r
        if n % x == 0:\r
            break\r
    else:\r
        # 循环没有被 break 中断时执行\r
        print(f"{n} 是质数")\r
\r
# ===== while 循环 =====\r
count = 0\r
while count < 5:\r
    print(count)\r
    count += 1\r
\r
# while-else（和 for-else 类似）\r
while condition:\r
    do_something()\r
else:\r
    # condition 变为 False 时执行（不是 break 退出）\r
    print("循环正常结束")\r
\r
# ===== break / continue / pass =====\r
for i in range(10):\r
    if i == 3:\r
        continue    # 跳过本次，进入下一次\r
    if i == 7:\r
        break       # 直接跳出循环\r
    print(i)        # 输出 0 1 2 4 5 6\r
\r
# pass 是空操作，用作占位符\r
class MyClass:\r
    pass            # 暂时不实现，先占位\r
\r
if condition:\r
    pass            # 稍后补充逻辑\r
\r
# ===== match-case（Python 3.10+ 结构化模式匹配） =====\r
# 比 if-elif 链更清晰，支持解构\r
match status_code:\r
    case 200:\r
        print("成功")\r
    case 404:\r
        print("未找到")\r
    case 500 | 502 | 503:     # 多个值匹配\r
        print("服务器错误")\r
    case code if code >= 400:  # 带守卫条件\r
        print(f"客户端错误：{code}")\r
    case _:                    # 默认分支（类似 default）\r
        print("未知状态")\r
\r
# 模式匹配支持解构\r
match command.split():\r
    case ["quit"]:\r
        print("退出")\r
    case ["go", direction]:\r
        print(f"向 {direction} 走")\r
    case ["go", direction, distance]:\r
        print(f"向 {direction} 走 {distance} 步")\r
\`\`\`\r
\r
> **for-else 的语义**：else 块在循环**没有被 break 中断**时执行。常被误解为"循环失败时执行"，实际是"循环正常完成时执行"。在搜索场景中非常有用：找到了就 break，没找到就走 else。\r
\r
---\r
\r
## 二、数据结构\r
\r
Python 内置四种数据结构：**列表（list）、元组（tuple）、字典（dict）、集合（set）**。它们各有特点，适用于不同场景。\r
\r
| 类型 | 有序 | 可变 | 重复 | 语法 | 用途 |\r
|------|------|------|------|------|------|\r
| list | ✅ | ✅ | ✅ | \`[1,2,3]\` | 通用有序集合 |\r
| tuple | ✅ | ❌ | ✅ | \`(1,2,3)\` | 不可变记录、字典键 |\r
| dict | ✅* | ✅ | 键唯一 | \`{"k":"v"}\` | 键值映射 |\r
| set | ❌ | ✅ | ❌ | \`{1,2,3}\` | 去重、集合运算 |\r
\r
> *Python 3.7+ dict 保持插入顺序\r
\r
### 2.1 列表 list\r
\r
列表是 Python 中最常用的数据结构，底层是**动态数组**（不是链表）。随机访问 O(1)，末尾增删 O(1) 均摊，中间插入删除 O(n)。\r
\r
\`\`\`python\r
# ===== 创建 =====\r
nums = [1, 2, 3, 4, 5]\r
empty = []\r
from_range = list(range(10))         # [0, 1, 2, ..., 9]\r
from_string = list("hello")          # ['h', 'e', 'l', 'l', 'o']\r
copy = nums.copy()                   # 浅拷贝\r
copy2 = nums[:]                      # 切片也是浅拷贝\r
\r
# ===== 访问与切片 =====\r
nums[0]        # 1（第一个）\r
nums[-1]       # 5（最后一个）\r
nums[1:3]      # [2, 3]（左闭右开）\r
nums[::2]      # [1, 3, 5]（步长 2）\r
nums[::-1]     # [5, 4, 3, 2, 1]（反转）\r
\r
# ===== 增删改 =====\r
nums.append(6)          # 末尾添加 → [1,2,3,4,5,6]\r
nums.extend([7, 8])     # 末尾扩展多个 → [1,2,3,4,5,6,7,8]\r
nums.insert(0, 0)       # 指定位置插入 → [0,1,2,3,4,5,6,7,8]\r
\r
nums.remove(3)          # 删除第一个值为 3 的元素（不存在会报错）\r
nums.pop()              # 弹出并返回末尾元素\r
nums.pop(0)             # 弹出并返回指定位置元素\r
del nums[0]             # 删除指定位置（不返回值）\r
del nums[1:3]           # 删除切片\r
\r
nums.clear()            # 清空列表 → []\r
\r
# ===== 查找 =====\r
3 in nums               # True（O(n) 线性查找）\r
nums.index(3)           # 返回第一个 3 的索引（不存在会报错）\r
nums.count(3)           # 统计 3 出现的次数\r
\r
# ===== 排序 =====\r
nums = [3, 1, 4, 1, 5, 9, 2, 6]\r
\r
nums.sort()                          # 原地升序 → [1,1,2,3,4,5,6,9]\r
nums.sort(reverse=True)              # 原地降序\r
sorted_nums = sorted(nums)           # 返回新列表，不修改原列表\r
sorted_nums = sorted(nums, reverse=True)\r
\r
# 自定义排序\r
words = ["banana", "apple", "cherry", "date"]\r
sorted(words, key=len)               # 按长度排序 → ['date', 'apple', 'banana', 'cherry']\r
sorted(words, key=str.lower)         # 忽略大小写\r
\r
# 按字典的某个键排序\r
users = [{"name": "Bob", "age": 30}, {"name": "Alice", "age": 25}]\r
sorted(users, key=lambda u: u["age"])         # 按年龄升序\r
sorted(users, key=lambda u: u["age"], reverse=True)  # 按年龄降序\r
\r
# ===== 列表推导式（Python 最强大的特性之一） =====\r
# 基本形式\r
squares = [x**2 for x in range(10)]\r
# 等价于：\r
# squares = []\r
# for x in range(10):\r
#     squares.append(x**2)\r
\r
# 带条件过滤\r
evens = [x for x in range(20) if x % 2 == 0]\r
\r
# 嵌套（展平二维列表）\r
matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]\r
flat = [x for row in matrix for x in row]   # [1,2,3,4,5,6,7,8,9]\r
\r
# 字典列表提取某个字段\r
names = [u["name"] for u in users]\r
\r
# 条件表达式在推导式中\r
labels = ["偶" if x % 2 == 0 else "奇" for x in range(10)]\r
\`\`\`\r
\r
> **性能提示**：频繁在列表头部插入/删除（\`insert(0, x)\` / \`pop(0)\`）效率很低，因为所有元素都要移动。如果需要频繁的头部操作，用 \`collections.deque\`（双端队列，两端操作都是 O(1)）。\r
\r
> **浅拷贝陷阱**：\`list.copy()\` 和 \`list[:]\` 都是浅拷贝——外层是新列表，但内部嵌套对象仍是引用。需要完全独立的副本用 \`copy.deepcopy()\`。\r
> \`\`\`python\r
> original = [[1, 2], [3, 4]]\r
> shallow = original.copy()\r
> shallow[0].append(99)   # original[0] 也变成了 [1, 2, 99]！\r
> \`\`\`\r
\r
### 2.2 元组 tuple\r
\r
元组是**不可变列表**。一旦创建就不能修改（增删改都不行）。\r
\r
**为什么要用元组？**\r
\r
1. **安全性**：数据不应被修改时用 tuple，防止意外篡改\r
2. **可哈希**：tuple 可以作为字典的键或 set 的元素（list 不行）\r
3. **性能**：创建速度和内存都优于 list（Python 内部还会缓存小 tuple）\r
4. **语义**：tuple 表示"结构/记录"（ heterogeneous ），list 表示"集合"（homogeneous）\r
\r
\`\`\`python\r
# 创建\r
point = (3, 4)\r
single = (42,)         # 单元素 tuple 必须加逗号！\r
empty = ()\r
no_parens = 1, 2, 3    # 不加括号也行（但不推荐）\r
\r
# 不可变\r
point[0] = 10          # TypeError!\r
\r
# 解包（unpacking）—— 非常常用\r
x, y = point\r
first, *rest = (1, 2, 3, 4, 5)    # first=1, rest=[2,3,4,5]\r
first, *middle, last = (1, 2, 3, 4, 5)  # middle=[2,3,4]\r
\r
# 交换变量（Python 特有的优雅写法）\r
a, b = 1, 2\r
a, b = b, a            # 不需要临时变量\r
\r
# 函数返回多个值（实际返回的是 tuple）\r
def divide(a, b):\r
    return a // b, a % b    # 返回 (商, 余)\r
\r
quotient, remainder = divide(17, 5)   # 3, 2\r
\r
# 命名元组 —— 给字段起名，增强可读性\r
from collections import namedtuple\r
\r
Point = namedtuple("Point", ["x", "y"])\r
p = Point(3, 4)\r
p.x          # 3\r
p.y          # 4\r
p[0]         # 3（仍然支持下标访问）\r
\r
# 比普通 tuple 可读性强得多\r
# 对比：\r
position = (3, 4)           # 不知道 3 和 4 分别是什么\r
position = Point(3, 4)      # 明确是 x=3, y=4\r
\`\`\`\r
\r
### 2.3 字典 dict\r
\r
字典是 Python 的灵魂数据结构，底层是**哈希表**。查找、插入、删除平均 O(1)。Python 3.7+ 保证保持插入顺序。\r
\r
\`\`\`python\r
# ===== 创建 =====\r
user = {"name": "Alice", "age": 25, "city": "Beijing"}\r
empty = {}\r
from_pairs = dict([("a", 1), ("b", 2)])    # 从键值对列表\r
from_keys = dict.fromkeys(["a", "b", "c"], 0)  # {'a':0, 'b':0, 'c':0}\r
\r
# ===== 访问 =====\r
user["name"]                  # "Alice"（键不存在会 KeyError）\r
user.get("email", "N/A")     # "N/A"（键不存在返回默认值，不报错）\r
user.get("email")             # None（不传默认值就返回 None）\r
\r
# setdefault：键不存在时设置默认值并返回\r
user.setdefault("tags", [])   # 键不存在 → 设置 [] 并返回\r
user["tags"].append("vip")    # 安全地操作\r
\r
# ===== 修改 =====\r
user["email"] = "a@b.com"    # 新增或修改\r
user.update({"age": 26, "phone": "123"})  # 批量更新\r
user |= {"age": 26}           # Python 3.9+ 合并运算符\r
\r
del user["city"]               # 删除（键不存在会 KeyError）\r
popped = user.pop("age")      # 弹出（键不存在会 KeyError）\r
popped = user.pop("age", None) # 弹出并指定默认值（不报错）\r
\r
# ===== 遍历 =====\r
for key in user:                # 遍历键（最常用）\r
    print(key)\r
\r
for key, value in user.items(): # 遍历键值对\r
    print(f"{key}: {value}")\r
\r
for value in user.values():     # 遍历值\r
    print(value)\r
\r
# 遍历中修改字典 → 必须先转成列表\r
for key in list(user.keys()):\r
    if key.startswith("_"):\r
        del user[key]\r
\r
# ===== 字典推导式 =====\r
squared = {x: x**2 for x in range(5)}\r
# {0: 0, 1: 1, 2: 4, 3: 9, 4: 16}\r
\r
# 翻转键值\r
flipped = {v: k for k, v in {"a": 1, "b": 2}.items()}\r
# {1: 'a', 2: 'b'}\r
\r
# 过滤\r
filtered = {k: v for k, v in user.items() if v is not None}\r
\r
# ===== 合并字典 =====\r
d1 = {"a": 1, "b": 2}\r
d2 = {"b": 3, "c": 4}\r
\r
# Python 3.9+（推荐）\r
merged = d1 | d2    # {'a': 1, 'b': 3, 'c': 4}（后者覆盖前者）\r
\r
# Python 3.5+\r
merged = {**d1, **d2}\r
\r
# 旧版\r
merged = d1.copy()\r
merged.update(d2)\r
\r
# ===== 嵌套字典 =====\r
config = {\r
    "database": {\r
        "host": "localhost",\r
        "port": 5432,\r
    },\r
    "redis": {\r
        "host": "localhost",\r
        "port": 6379,\r
    },\r
}\r
\r
# 安全访问嵌套字典\r
db_host = config.get("database", {}).get("host", "localhost")\r
\r
# ===== defaultdict（自动初始化的字典） =====\r
from collections import defaultdict\r
\r
# 按类别分组\r
groups = defaultdict(list)\r
for item in ["apple", "banana", "cherry", "avocado"]:\r
    groups[item[0]].append(item)\r
# {'a': ['apple', 'avocado'], 'b': ['banana'], 'c': ['cherry']}\r
\r
# 计数\r
counter = defaultdict(int)\r
for word in ["apple", "banana", "apple", "cherry", "apple"]:\r
    counter[word] += 1\r
# {'apple': 3, 'banana': 1, 'cherry': 1}\r
# 其实直接用 collections.Counter 更方便\r
\r
# ===== OrderedDict（需要精确控制顺序时用） =====\r
from collections import OrderedDict\r
# Python 3.7+ 普通 dict 已经保序，OrderedDict 的额外价值：\r
# - move_to_end() 移动键到末尾/开头\r
# - popitem(last=True/False) 从末尾/开头弹出\r
# - 相等性比较考虑顺序\r
\`\`\`\r
\r
> **dict 键的要求**：键必须是**可哈希的**（hashable）。不可变类型（str, int, float, tuple, frozenset）可以，可变类型（list, dict, set）不行。\r
\r
### 2.4 集合 set\r
\r
集合是**无序、不重复**的元素容器，底层也是哈希表。主要用于：去重、成员测试（O(1)）、集合运算。\r
\r
\`\`\`python\r
# ===== 创建 =====\r
fruits = {"apple", "banana", "cherry"}\r
from_list = set([1, 2, 2, 3, 3, 3])   # {1, 2, 3}\r
empty = set()     # 注意：{} 创建的是空字典，不是空集合！\r
\r
# ===== 增删 =====\r
fruits.add("orange")\r
fruits.update(["grape", "mango"])    # 添加多个\r
fruits.remove("banana")              # 删除（不存在会 KeyError）\r
fruits.discard("xyz")                # 删除（不存在不报错）\r
fruits.pop()                         # 随机弹出一个\r
\r
# ===== 成员测试（比 list 快得多） =====\r
"apple" in fruits    # O(1)，而 list 是 O(n)\r
\r
# 当需要频繁判断"某元素是否存在"时，把 list 转成 set\r
valid_ids = set([1, 5, 10, 20, 50])\r
if user_id in valid_ids:    # 比 list 快几个数量级\r
    ...\r
\r
# ===== 集合运算 =====\r
a = {1, 2, 3, 4}\r
b = {3, 4, 5, 6}\r
\r
a | b    # {1, 2, 3, 4, 5, 6}   并集（a.union(b)）\r
a & b    # {3, 4}                交集（a.intersection(b)）\r
a - b    # {1, 2}                差集（a.difference(b)）\r
b - a    # {5, 6}                差集方向不同\r
a ^ b    # {1, 2, 5, 6}          对称差集（不共有的元素）\r
\r
# 子集/超集\r
{1, 2} <= {1, 2, 3}     # True（子集）\r
{1, 2, 3} >= {1, 2}     # True（超集）\r
\r
# ===== 实际应用 =====\r
# 1. 列表去重\r
nums = [1, 2, 2, 3, 3, 3, 4]\r
unique = list(set(nums))   # [1, 2, 3, 4]（注意：顺序可能变）\r
\r
# 保序去重（Python 3.7+）\r
unique_ordered = list(dict.fromkeys(nums))   # [1, 2, 3, 4]\r
\r
# 2. 找出两个列表的共同元素 / 差异\r
list_a = ["Alice", "Bob", "Charlie"]\r
list_b = ["Bob", "David", "Charlie"]\r
common = set(list_a) & set(list_b)     # {"Bob", "Charlie"}\r
only_a = set(list_a) - set(list_b)     # {"Alice"}\r
\r
# 3. 集合推导式\r
lengths = {len(word) for word in ["apple", "banana", "cherry", "date"]}\r
# {5, 6, 4}\r
\r
# ===== frozenset（不可变集合） =====\r
fs = frozenset([1, 2, 3])\r
fs.add(4)    # AttributeError！\r
# 用途：作为字典的键或放入另一个 set 中\r
\`\`\`\r
\r
---\r
\r
## 三、函数与模块\r
\r
### 3.1 函数定义\r
\r
函数是组织代码的基本单元。Python 的函数是一等公民（first-class），可以赋值给变量、作为参数传递、从函数中返回。\r
\r
\`\`\`python\r
def greet(name: str, greeting: str = "你好") -> str:\r
    """\r
    向指定人员打招呼。\r
    \r
    参数：\r
        name: 人员姓名\r
        greeting: 问候语，默认"你好"\r
    \r
    返回：\r
        完整的问候字符串\r
    \r
    示例：\r
        >>> greet("Alice")\r
        '你好，Alice！'\r
        >>> greet("Bob", "早上好")\r
        '早上好，Bob！'\r
    """\r
    return f"{greeting}，{name}！"\r
\r
# 调用方式\r
greet("Alice")                    # 位置参数\r
greet("Bob", "早上好")             # 两个位置参数\r
greet(name="Charlie")             # 关键字参数\r
greet(greeting="晚安", name="David")  # 关键字参数可以不按顺序\r
\`\`\`\r
\r
> **docstring 规范**：第一个行是简短描述，空一行后是详细说明。推荐用 Google 风格或 Sphinx 风格的文档格式。IDE 会在调用时显示 docstring。\r
\r
**返回多个值**\r
\r
\`\`\`python\r
def get_user_info(user_id: int) -> tuple[str, int, str]:\r
    """返回 (姓名, 年龄, 邮箱)"""\r
    # ... 查询数据库\r
    return "Alice", 25, "alice@example.com"\r
\r
# 解包接收\r
name, age, email = get_user_info(1)\r
\r
# 或者接收为 tuple\r
info = get_user_info(1)\r
print(info[0])   # "Alice"\r
\`\`\`\r
\r
### 3.2 参数类型详解\r
\r
Python 函数的参数系统非常灵活，也是最容易让人困惑的部分。参数传递的完整顺序规则：\r
\r
\`\`\`\r
位置参数 → 默认参数 → *args → 关键字参数 → **kwargs\r
\`\`\`\r
\r
\`\`\`python\r
def func(a, b, *args, key="default", **kwargs):\r
    """\r
    a, b       - 必须的位置参数\r
    *args      - 额外的位置参数，收集为 tuple\r
    key        - 关键字参数（有默认值）\r
    **kwargs   - 额外的关键字参数，收集为 dict\r
    """\r
    print(f"a={a}, b={b}")\r
    print(f"args={args}")           # tuple\r
    print(f"key={key}")\r
    print(f"kwargs={kwargs}")       # dict\r
\r
func(1, 2, 3, 4, 5, key="custom", extra="hello")\r
# a=1, b=2\r
# args=(3, 4, 5)\r
# key=custom\r
# kwargs={'extra': 'hello'}\r
\`\`\`\r
\r
**参数限定符（Python 3.8+）**\r
\r
\`\`\`python\r
def func(pos_only, /, any_kind, *, kw_only):\r
    """\r
    pos_only  - 只能按位置传递（/ 之前）\r
    any_kind  - 位置或关键字都行（/ 和 * 之间）\r
    kw_only   - 只能按关键字传递（* 之后）\r
    """\r
    pass\r
\r
func(1, 2, kw_only=3)         # ✅\r
func(1, any_kind=2, kw_only=3) # ✅\r
func(pos_only=1, ...)          # ❌ TypeError\r
func(1, 2, 3)                  # ❌ kw_only 必须用关键字\r
\`\`\`\r
\r
**参数解包**\r
\r
\`\`\`python\r
def add(a, b, c):\r
    return a + b + c\r
\r
# 用 * 解包列表/元组为位置参数\r
args = [1, 2, 3]\r
add(*args)    # 等价于 add(1, 2, 3)\r
\r
# 用 ** 解包字典为关键字参数\r
kwargs = {"a": 1, "b": 2, "c": 3}\r
add(**kwargs)  # 等价于 add(a=1, b=2, c=3)\r
\`\`\`\r
\r
### 3.3 Lambda 与高阶函数\r
\r
\`\`\`python\r
# lambda 是匿名函数，只能写一行表达式\r
square = lambda x: x ** 2\r
add = lambda a, b: a + b\r
full_name = lambda u: f"{u['first']} {u['last']}"\r
\r
# lambda 最常见的用途是给 sorted/map/filter 等提供 key 函数\r
users = [{"name": "Bob", "age": 30}, {"name": "Alice", "age": 25}]\r
sorted(users, key=lambda u: u["age"])    # 按年龄排序\r
\r
# ===== map / filter / reduce =====\r
nums = [1, 2, 3, 4, 5]\r
\r
# map：对每个元素应用函数\r
doubled = list(map(lambda x: x * 2, nums))    # [2, 4, 6, 8, 10]\r
# 更推荐用推导式：\r
doubled = [x * 2 for x in nums]               # 同样的效果，更 Pythonic\r
\r
# filter：过滤元素\r
evens = list(filter(lambda x: x % 2 == 0, nums))  # [2, 4]\r
# 更推荐用推导式：\r
evens = [x for x in nums if x % 2 == 0]\r
\r
# reduce：累积归约（需要从 functools 导入）\r
from functools import reduce\r
total = reduce(lambda a, b: a + b, nums)       # 15\r
# 等价于 sum(nums)，但 reduce 可以做更复杂的归约\r
\r
# ===== functools 常用工具 =====\r
from functools import partial, lru_cache, wraps\r
\r
# partial：固定部分参数，创建新函数\r
def power(base, exponent):\r
    return base ** exponent\r
\r
square = partial(power, exponent=2)\r
cube = partial(power, exponent=3)\r
square(5)   # 25\r
cube(5)     # 125\r
\r
# lru_cache：函数结果缓存（记忆化，对递归和重复计算非常有用）\r
@lru_cache(maxsize=128)\r
def fibonacci(n):\r
    if n < 2:\r
        return n\r
    return fibonacci(n - 1) + fibonacci(n - 2)\r
\r
fibonacci(100)   # 瞬间完成，没有缓存会算到天荒地老\r
\`\`\`\r
\r
### 3.4 闭包\r
\r
闭包 = 内层函数 + 它引用的外层函数变量。闭包让函数能"记住"创建时的环境。\r
\r
\`\`\`python\r
def make_multiplier(factor):\r
    """创建一个乘法函数"""\r
    def multiplier(x):\r
        return x * factor    # factor 来自外层函数（被"闭合"捕获）\r
    return multiplier\r
\r
double = make_multiplier(2)\r
triple = make_multiplier(3)\r
\r
double(5)    # 10\r
triple(5)    # 15\r
\r
# 闭包的经典应用：计数器\r
def make_counter():\r
    count = 0\r
    def counter():\r
        nonlocal count     # 声明要修改外层变量\r
        count += 1\r
        return count\r
    return counter\r
\r
c = make_counter()\r
c()    # 1\r
c()    # 2\r
c()    # 3\r
\`\`\`\r
\r
> **闭包陷阱**：循环中创建闭包时，所有闭包共享同一个变量的引用。\r
> \`\`\`python\r
> # 错误：所有函数都返回最后一个 i 的值\r
> funcs = [lambda: i for i in range(5)]\r
> [f() for f in funcs]   # [4, 4, 4, 4, 4]\r
>\r
> # 正确：用默认参数绑定当前值\r
> funcs = [lambda i=i: i for i in range(5)]\r
> [f() for f in funcs]   # [0, 1, 2, 3, 4]\r
> \`\`\`\r
\r
### 3.5 模块与包\r
\r
\`\`\`python\r
# ===== 导入方式 =====\r
import os                           # 导入整个模块\r
from pathlib import Path            # 导入特定对象\r
from datetime import datetime as dt # 别名\r
from mypackage import func1, func2  # 从包导入\r
\r
# ===== __name__ 的作用 =====\r
# 每个 Python 文件都有 __name__ 属性\r
# 直接运行时 __name__ == "__main__"\r
# 被 import 时 __name__ == 模块名\r
\r
if __name__ == "__main__":\r
    # 只有直接运行此文件时才执行，被 import 时不执行\r
    # 常用于测试代码或启动入口\r
    main()\r
\r
# ===== __all__ 控制 from module import * =====\r
# 在模块顶部定义 __all__，指定 import * 时导出哪些名字\r
__all__ = ["public_func", "PublicClass"]\r
\r
def public_func():    # 会被导出\r
    pass\r
\r
def _internal():      # 不会导出（下划线开头也不会）\r
    pass\r
\r
# ===== 包结构 =====\r
# mypackage/\r
# ├── __init__.py        # 包的初始化（可以为空，也可以放初始化代码）\r
# ├── module_a.py        # from mypackage import module_a\r
# ├── module_b.py        # from mypackage.module_b import some_func\r
# └── subpackage/\r
#     ├── __init__.py\r
#     └── module_c.py    # from mypackage.subpackage.module_c import ...\r
\r
# ===== 避免循环导入 =====\r
# a.py: from b import func_b\r
# b.py: from a import func_a  → ImportError!\r
#\r
# 解决方案：\r
# 1. 重构：把共用代码提取到第三个模块 c.py\r
# 2. 延迟导入：在函数内部 import（不推荐）\r
# 3. 用 import module 代替 from module import name\r
\`\`\`\r
\r
---\r
\r
## 四、面向对象编程\r
\r
### 4.1 类的基本定义\r
\r
\`\`\`python\r
class Animal:\r
    """动物基类 —— 演示类的核心概念"""\r
    \r
    # 类变量：所有实例共享（谨慎使用，通常用常量）\r
    kingdom = "动物界"\r
    _count = 0    # 下划线开头表示"私有"（约定，非强制）\r
    \r
    def __init__(self, name: str, age: int):\r
        """构造方法：创建实例时自动调用"""\r
        self.name = name          # 实例属性（公有）\r
        self._internal = "约定私有"  # 约定私有（外部仍可以访问）\r
        self.__private = "名称改写"  # 双下划线触发 name mangling\r
        Animal._count += 1\r
    \r
    def speak(self) -> str:\r
        """实例方法：第一个参数必须是 self"""\r
        return f"{self.name}在叫"\r
    \r
    def __str__(self) -> str:\r
        """print(obj) 时调用"""\r
        return f"Animal({self.name}, {self.age}岁)"\r
    \r
    def __repr__(self) -> str:\r
        """在 REPL 中直接显示 obj 时调用，应返回可重建对象的字符串"""\r
        return f"Animal(name={self.name!r}, age={self.age})"\r
\r
# 创建实例\r
dog = Animal("旺财", 3)\r
print(dog)          # Animal(旺财, 3岁)\r
repr(dog)           # "Animal(name='旺财', age=3)"\r
\`\`\`\r
\r
### 4.2 继承与多态\r
\r
\`\`\`python\r
class Dog(Animal):\r
    def __init__(self, name: str, age: int, breed: str):\r
        super().__init__(name, age)   # 调用父类构造\r
        self.breed = breed\r
    \r
    def speak(self) -> str:           # 方法重写\r
        return f"{self.name}：汪汪汪！"\r
\r
class Cat(Animal):\r
    def speak(self) -> str:\r
        return f"{self.name}：喵喵喵~"\r
\r
# 多态：同一个接口，不同实现\r
animals: list[Animal] = [Dog("旺财", 3, "柴犬"), Cat("咪咪", 2)]\r
for animal in animals:\r
    print(animal.speak())    # 不需要知道具体类型，各自调用自己的 speak\r
\r
# isinstance 检查（考虑继承）\r
isinstance(dog, Dog)       # True\r
isinstance(dog, Animal)    # True（子类也是父类类型）\r
\`\`\`\r
\r
### 4.3 类方法、静态方法、属性\r
\r
\`\`\`python\r
class Circle:\r
    _pi = 3.14159\r
    \r
    def __init__(self, radius: float):\r
        self._radius = radius\r
    \r
    @property\r
    def radius(self) -> float:\r
        """像属性一样访问，实际执行方法（getter）"""\r
        return self._radius\r
    \r
    @radius.setter\r
    def radius(self, value: float):\r
        """赋值时触发（setter），可以加验证逻辑"""\r
        if value < 0:\r
            raise ValueError("半径不能为负")\r
        self._radius = value\r
    \r
    @property\r
    def area(self) -> float:\r
        """只读属性（只有 getter 没有 setter）"""\r
        return self._pi * self._radius ** 2\r
    \r
    @classmethod\r
    def from_diameter(cls, diameter: float) -> "Circle":\r
        """工厂方法：用类方法创建实例的替代构造方式"""\r
        return cls(diameter / 2)\r
    \r
    @staticmethod\r
    def is_valid_radius(value: float) -> bool:\r
        """静态方法：不需要 self 或 cls，纯粹是工具函数"""\r
        return value >= 0\r
\r
c = Circle(5)\r
c.radius        # 5（触发 getter）\r
c.radius = 10   # 触发 setter\r
c.area          # 314.159（只读属性）\r
c2 = Circle.from_diameter(10)   # 工厂方法\r
\`\`\`\r
\r
### 4.4 dataclass（Python 3.7+，强烈推荐）\r
\r
当你需要一个主要用来存储数据的类时，\`@dataclass\` 可以省去大量样板代码。它会自动生成 \`__init__\`、\`__repr__\`、\`__eq__\` 等方法。\r
\r
\`\`\`python\r
from dataclasses import dataclass, field\r
\r
@dataclass\r
class User:\r
    name: str\r
    age: int\r
    email: str = ""\r
    tags: list[str] = field(default_factory=list)\r
    \r
    @property\r
    def is_adult(self) -> bool:\r
        return self.age >= 18\r
\r
# 自动生成 __init__、__repr__、__eq__\r
u = User("Alice", 25, "a@b.com")\r
print(u)   # User(name='Alice', age=25, email='a@b.com', tags=[])\r
\r
# 常用选项\r
@dataclass(frozen=True)    # 不可变（类似 namedtuple 但更灵活）\r
class Point:\r
    x: float\r
    y: float\r
\r
@dataclass(order=True)     # 自动生成 __lt__ __le__ __gt__ __ge__\r
class Student:\r
    grade: float\r
    name: str\r
\r
# frozen=True 后可以做 dict 的 key\r
p = Point(1.0, 2.0)\r
d = {p: "origin area"}\r
\`\`\`\r
\r
### 4.5 魔术方法（dunder methods）\r
\r
魔术方法让你的类能像内置类型一样工作。\r
\r
\`\`\`python\r
class Vector:\r
    def __init__(self, x: float, y: float):\r
        self.x = x\r
        self.y = y\r
    \r
    def __repr__(self):\r
        return f"Vector({self.x}, {self.y})"\r
    \r
    def __add__(self, other):          # v1 + v2\r
        return Vector(self.x + other.x, self.y + other.y)\r
    \r
    def __mul__(self, scalar):         # v * 3\r
        return Vector(self.x * scalar, self.y * scalar)\r
    \r
    def __abs__(self):                 # abs(v)\r
        return (self.x**2 + self.y**2) ** 0.5\r
    \r
    def __eq__(self, other):           # v1 == v2\r
        return self.x == other.x and self.y == other.y\r
    \r
    def __len__(self):                 # len(v) —— 语义自定义\r
        return 2\r
    \r
    def __getitem__(self, idx):        # v[0], v[1]\r
        return (self.x, self.y)[idx]\r
    \r
    def __iter__(self):                # 支持 for x in v\r
        yield self.x\r
        yield self.y\r
    \r
    def __contains__(self, value):     # 0 in v\r
        return value in (self.x, self.y)\r
\r
v1 = Vector(1, 2)\r
v2 = Vector(3, 4)\r
v1 + v2       # Vector(4, 6)\r
v1 * 3        # Vector(3, 6)\r
abs(v2)       # 5.0\r
v1[0]         # 1\r
list(v1)      # [1, 2]\r
\`\`\`\r
\r
> **常用魔术方法速查**：\r
> - 字符串：\`__str__\`（用户友好）、\`__repr__\`（开发者友好）\r
> - 比较：\`__eq__\`、\`__lt__\`、\`__le__\`、\`__gt__\`、\`__ge__\`\r
> - 算术：\`__add__\`、\`__sub__\`、\`__mul__\`、\`__truediv__\`、\`__floordiv__\`、\`__mod__\`\r
> - 容器：\`__len__\`、\`__getitem__\`、\`__setitem__\`、\`__contains__\`、\`__iter__\`\r
> - 上下文：\`__enter__\`、\`__exit__\`（支持 with 语句）\r
> - 调用：\`__call__\`（让实例可以像函数一样被调用）\r
\r
### 4.6 抽象基类\r
\r
\`\`\`python\r
from abc import ABC, abstractmethod\r
\r
class Shape(ABC):\r
    """抽象基类：定义接口规范，子类必须实现所有抽象方法"""\r
    \r
    @abstractmethod\r
    def area(self) -> float:\r
        """计算面积（子类必须实现）"""\r
        pass\r
    \r
    @abstractmethod\r
    def perimeter(self) -> float:\r
        """计算周长（子类必须实现）"""\r
        pass\r
    \r
    # 可以有普通方法作为默认实现\r
    def describe(self) -> str:\r
        return f"{self.__class__.__name__}: 面积={self.area():.2f}"\r
\r
class Rectangle(Shape):\r
    def __init__(self, width: float, height: float):\r
        self.width = width\r
        self.height = height\r
    \r
    def area(self) -> float:\r
        return self.width * self.height\r
    \r
    def perimeter(self) -> float:\r
        return 2 * (self.width + self.height)\r
\r
# Shape()  # TypeError! 不能实例化抽象类\r
r = Rectangle(3, 4)\r
r.describe()   # "Rectangle: 面积=12.00"\r
\`\`\`\r
\r
---\r
\r
## 五、文件与 IO\r
\r
### 5.1 文件读写\r
\r
\`\`\`python\r
# ===== 推荐写法：with 语句（自动关闭，即使发生异常） =====\r
with open("data.txt", "r", encoding="utf-8") as f:\r
    content = f.read()           # 读取全部内容为一个字符串\r
\r
# 逐行读取（内存友好，适合大文件）\r
with open("data.txt", "r", encoding="utf-8") as f:\r
    for line in f:\r
        process(line.strip())\r
\r
# 读取所有行为列表\r
with open("data.txt", "r", encoding="utf-8") as f:\r
    lines = f.readlines()        # 每行包含 \\n，需要 strip()\r
\r
# ===== 写入 =====\r
with open("output.txt", "w", encoding="utf-8") as f:\r
    f.write("第一行\\n")\r
    f.writelines(["第二行\\n", "第三行\\n"])\r
\r
# 追加模式\r
with open("log.txt", "a", encoding="utf-8") as f:\r
    f.write("新日志\\n")\r
\r
# ===== 文件模式 =====\r
# "r"  只读（默认）\r
# "w"  写入（覆盖已有内容！）\r
# "a"  追加\r
# "x"  创建新文件（文件已存在会报错）\r
# "b"  二进制模式（配合 rb/wb 读写图片等）\r
# "r+" 读写\r
\`\`\`\r
\r
> **编码问题**：Windows 默认编码是 GBK，Linux/Mac 是 UTF-8。**永远显式指定 \`encoding="utf-8"\`**，否则跨平台时会出乱码。\r
\r
### 5.2 pathlib（现代路径操作，推荐）\r
\r
\`pathlib\` 是 Python 3 推荐的路径操作库，比 \`os.path\` 更优雅。\r
\r
\`\`\`python\r
from pathlib import Path\r
\r
# ===== 创建路径 =====\r
p = Path("data/subdir/file.txt")\r
p = Path.home() / "documents" / "report.pdf"    # 用 / 拼接路径\r
p = Path.cwd()                                    # 当前工作目录\r
\r
# ===== 路径组成部分 =====\r
p = Path("/home/user/data/file.txt")\r
p.name       # "file.txt"（文件名）\r
p.stem       # "file"（不含扩展名）\r
p.suffix     # ".txt"（扩展名）\r
p.parent     # Path("/home/user/data")\r
\r
# ===== 查询 =====\r
p.exists()          # 是否存在\r
p.is_file()         # 是否为文件\r
p.is_dir()          # 是否为目录\r
p.stat()            # 文件信息（大小、修改时间等）\r
p.stat().st_size    # 文件大小（字节）\r
\r
# ===== 创建/删除 =====\r
p.parent.mkdir(parents=True, exist_ok=True)   # 创建目录（含父级）\r
p.touch()                                      # 创建空文件\r
p.unlink()                                     # 删除文件\r
Path("empty_dir").rmdir()                      # 删除空目录\r
\r
# ===== 遍历目录 =====\r
for item in Path(".").iterdir():        # 当前目录下的文件和文件夹\r
    print(item)\r
\r
for py_file in Path(".").glob("*.py"):  # 当前目录下的 .py 文件\r
    print(py_file)\r
\r
for py_file in Path(".").glob("**/*.py"):  # 递归查找所有 .py\r
    print(py_file)\r
\r
# ===== 读写快捷方法 =====\r
Path("data.txt").write_text("内容", encoding="utf-8")\r
text = Path("data.txt").read_text(encoding="utf-8")\r
data = Path("data.bin").read_bytes()\r
\`\`\`\r
\r
### 5.3 JSON / CSV\r
\r
\`\`\`python\r
import json\r
import csv\r
\r
# ===== JSON =====\r
data = {"name": "Alice", "scores": [90, 85, 92], "active": True}\r
\r
# 序列化\r
json_str = json.dumps(data, ensure_ascii=False, indent=2)\r
# ensure_ascii=False 才能正确输出中文\r
# indent=2 格式化输出（生产环境不传，节省空间）\r
\r
# 反序列化\r
parsed = json.loads(json_str)\r
\r
# 文件读写\r
with open("data.json", "w", encoding="utf-8") as f:\r
    json.dump(data, f, ensure_ascii=False, indent=2)\r
\r
with open("data.json", "r", encoding="utf-8") as f:\r
    data = json.load(f)\r
\r
# 处理日期等特殊类型\r
from datetime import datetime\r
class DateEncoder(json.JSONEncoder):\r
    def default(self, obj):\r
        if isinstance(obj, datetime):\r
            return obj.isoformat()\r
        return super().default(obj)\r
\r
json.dumps({"now": datetime.now()}, cls=DateEncoder)\r
\r
# ===== CSV =====\r
# 写入\r
with open("data.csv", "w", newline="", encoding="utf-8-sig") as f:\r
    writer = csv.writer(f)\r
    writer.writerow(["姓名", "年龄", "城市"])\r
    writer.writerow(["Alice", 25, "北京"])\r
    writer.writerows([["Bob", 30, "上海"], ["Charlie", 35, "广州"]])\r
\r
# 读取\r
with open("data.csv", "r", encoding="utf-8-sig") as f:\r
    reader = csv.DictReader(f)    # 用第一行做键名\r
    for row in reader:\r
        print(row["姓名"], row["年龄"])\r
\r
# 注意：encoding="utf-8-sig" 可以正确处理带 BOM 的 CSV（Excel 保存的）\r
\`\`\`\r
\r
---\r
\r
## 六、异常处理\r
\r
### 6.1 基本语法\r
\r
\`\`\`python\r
try:\r
    result = 10 / 0\r
except ZeroDivisionError as e:\r
    print(f"除零错误：{e}")\r
except (TypeError, ValueError) as e:\r
    print(f"类型或值错误：{e}")\r
except Exception as e:\r
    # 兜底：捕获所有常规异常（不推荐滥用）\r
    print(f"其他错误：{e}")\r
else:\r
    # 没有异常时执行（很少用但很有用）\r
    print("计算成功")\r
finally:\r
    # 始终执行（清理资源）\r
    print("清理完毕")\r
\`\`\`\r
\r
### 6.2 异常层次结构\r
\r
\`\`\`\r
BaseException\r
├── SystemExit          # sys.exit() 触发\r
├── KeyboardInterrupt   # Ctrl+C\r
├── GeneratorExit       # 生成器关闭\r
└── Exception           # 所有常规异常的基类\r
    ├── ValueError\r
    ├── TypeError\r
    ├── KeyError\r
    ├── IndexError\r
    ├── FileNotFoundError\r
    ├── IOError / OSError\r
    └── ...\r
\`\`\`\r
\r
> **最佳实践**：只捕获 \`Exception\` 及其子类，不要裸 \`except:\` 或 \`except BaseException:\`，否则会吞掉 \`SystemExit\` 和 \`KeyboardInterrupt\`。\r
\r
### 6.3 自定义异常\r
\r
\`\`\`python\r
class BizException(Exception):\r
    """业务异常基类"""\r
    def __init__(self, message: str, code: int = 400):\r
        super().__init__(message)\r
        self.code = code\r
        self.message = message\r
\r
class NotFoundException(BizException):\r
    def __init__(self, resource: str, id: int):\r
        super().__init__(f"{resource} #{id} 不存在", code=404)\r
\r
class ValidationError(BizException):\r
    def __init__(self, field: str, reason: str):\r
        super().__init__(f"字段 {field} 验证失败：{reason}", code=422)\r
\r
# 使用\r
raise NotFoundException("用户", 42)\r
raise ValidationError("email", "格式不正确")\r
\r
# 捕获特定业务异常\r
try:\r
    process_order(order_id)\r
except NotFoundException as e:\r
    return {"error": e.message}, e.code\r
except ValidationError as e:\r
    return {"error": e.message, "field": "order"}, e.code\r
except BizException as e:\r
    return {"error": e.message}, e.code\r
\`\`\`\r
\r
### 6.4 上下文管理器\r
\r
\`\`\`python\r
# 类实现\r
class FileManager:\r
    def __init__(self, filename, mode="r"):\r
        self.filename = filename\r
        self.mode = mode\r
    \r
    def __enter__(self):\r
        self.file = open(self.filename, self.mode, encoding="utf-8")\r
        return self.file\r
    \r
    def __exit__(self, exc_type, exc_val, exc_tb):\r
        self.file.close()\r
        return False  # 不吞掉异常\r
\r
with FileManager("data.txt") as f:\r
    content = f.read()\r
\r
# contextlib 简化写法\r
from contextlib import contextmanager\r
\r
@contextmanager\r
def timer(label: str):\r
    """计时上下文管理器"""\r
    import time\r
    start = time.perf_counter()\r
    yield  # 进入 with 块\r
    elapsed = time.perf_counter() - start\r
    print(f"{label}: {elapsed:.4f}s")\r
\r
with timer("数据库查询"):\r
    query_database()\r
\r
@contextmanager\r
def temp_directory():\r
    """临时目录：退出时自动删除"""\r
    import tempfile, shutil\r
    tmpdir = tempfile.mkdtemp()\r
    try:\r
        yield tmpdir\r
    finally:\r
        shutil.rmtree(tmpdir, ignore_errors=True)\r
\`\`\`\r
\r
---\r
\r
## 七、进阶特性\r
\r
### 7.1 装饰器\r
\r
装饰器是 Python 最强大的特性之一。本质是一个接收函数并返回函数的高阶函数。\r
\r
\`\`\`python\r
import functools\r
import time\r
\r
# ===== 基本装饰器 =====\r
def timer(func):\r
    """计算函数执行时间"""\r
    @functools.wraps(func)   # 保留原函数的 __name__ 和 __doc__\r
    def wrapper(*args, **kwargs):\r
        start = time.perf_counter()\r
        result = func(*args, **kwargs)\r
        elapsed = time.perf_counter() - start\r
        print(f"{func.__name__} 耗时 {elapsed:.4f}s")\r
        return result\r
    return wrapper\r
\r
@timer    # 等价于 slow_function = timer(slow_function)\r
def slow_function():\r
    time.sleep(1)\r
    return "done"\r
\r
# ===== 带参数的装饰器 =====\r
def retry(max_attempts=3, delay=1):\r
    """失败重试装饰器"""\r
    def decorator(func):\r
        @functools.wraps(func)\r
        def wrapper(*args, **kwargs):\r
            for attempt in range(max_attempts):\r
                try:\r
                    return func(*args, **kwargs)\r
                except Exception as e:\r
                    if attempt == max_attempts - 1:\r
                        raise\r
                    print(f"第 {attempt+1} 次失败：{e}，{delay}s 后重试...")\r
                    time.sleep(delay)\r
        return wrapper\r
    return decorator\r
\r
@retry(max_attempts=5, delay=2)\r
def unstable_api_call():\r
    pass\r
\r
# ===== 类装饰器 =====\r
def singleton(cls):\r
    """让类变成单例模式"""\r
    instances = {}\r
    @functools.wraps(cls)\r
    def get_instance(*args, **kwargs):\r
        if cls not in instances:\r
            instances[cls] = cls(*args, **kwargs)\r
        return instances[cls]\r
    return get_instance\r
\r
@singleton\r
class Database:\r
    def __init__(self):\r
        print("创建数据库连接...")\r
\r
db1 = Database()   # 创建数据库连接...\r
db2 = Database()   # 不输出（复用 db1）\r
db1 is db2         # True\r
\`\`\`\r
\r
> **\`@functools.wraps\` 必须加**：不加的话，被装饰的函数的 \`__name__\`、\`__doc__\` 会变成 wrapper 的，导致调试困难、文档丢失。\r
\r
### 7.2 生成器与迭代器\r
\r
\`\`\`python\r
# ===== 生成器函数 =====\r
def fibonacci(n):\r
    """生成斐波那契数列"""\r
    a, b = 0, 1\r
    for _ in range(n):\r
        yield a\r
        a, b = b, a + b\r
\r
list(fibonacci(10))   # [0, 1, 1, 2, 3, 5, 8, 13, 21, 34]\r
\r
# 生成器的核心价值：惰性求值，不一次性加载所有数据到内存\r
def read_large_file(filepath):\r
    """逐行读取大文件（不会把整个文件加载到内存）"""\r
    with open(filepath, "r", encoding="utf-8") as f:\r
        for line in f:\r
            yield line.strip()\r
\r
# 处理 10GB 的文件也不会 OOM\r
for line in read_large_file("huge_data.csv"):\r
    process(line)\r
\r
# ===== 生成器表达式 =====\r
squares_list = [x**2 for x in range(1000000)]    # 列表：立即占用大量内存\r
squares_gen  = (x**2 for x in range(1000000))    # 生成器：几乎不占内存\r
\r
# ===== yield from（委托给另一个生成器） =====\r
def flatten(nested):\r
    """展平嵌套列表"""\r
    for item in nested:\r
        if isinstance(item, list):\r
            yield from flatten(item)    # 委托给递归调用\r
        else:\r
            yield item\r
\r
list(flatten([1, [2, 3], [4, [5, 6]]]))   # [1, 2, 3, 4, 5, 6]\r
\`\`\`\r
\r
### 7.3 类型提示进阶\r
\r
\`\`\`python\r
from typing import TypeVar, Generic, Protocol, Callable, Any\r
from collections.abc import Sequence, Iterator\r
\r
# ===== 泛型 =====\r
T = TypeVar("T")\r
\r
class Stack(Generic[T]):\r
    def __init__(self):\r
        self._items: list[T] = []\r
    \r
    def push(self, item: T) -> None:\r
        self._items.append(item)\r
    \r
    def pop(self) -> T:\r
        return self._items.pop()\r
    \r
    def peek(self) -> T:\r
        return self._items[-1]\r
\r
int_stack = Stack[int]()\r
int_stack.push(42)\r
# int_stack.push("hello")  # mypy 会报错\r
\r
# ===== Protocol（鸭子类型的形式化） =====\r
class Drawable(Protocol):\r
    def draw(self) -> None:\r
        ...\r
\r
def render(obj: Drawable):   # 任何有 draw() 方法的对象都能传入\r
    obj.draw()\r
\r
# 不需要显式继承 Drawable，只要有 draw 方法就行\r
class Circle:\r
    def draw(self) -> None:\r
        print("画圆")\r
\r
render(Circle())   # ✅ 类型检查通过\r
\r
# ===== 常用类型别名 =====\r
from typing import Union, Optional\r
\r
def process(value: str | int | None) -> str:    # Python 3.10+ 联合类型\r
    if value is None:\r
        return "空"\r
    return str(value)\r
\r
# 回调类型\r
def apply(func: Callable[[int, int], int], a: int, b: int) -> int:\r
    return func(a, b)\r
\r
apply(lambda x, y: x + y, 3, 4)   # 7\r
\`\`\`\r
\r
---\r
\r
## 八、并发编程\r
\r
### 8.1 GIL（全局解释器锁）\r
\r
Python 的 GIL 是理解并发编程的关键：**同一时刻只有一个线程执行 Python 字节码**。\r
\r
- **CPU 密集型**（计算多）：用 \`multiprocessing\`（多进程，绕过 GIL）\r
- **IO 密集型**（网络/文件操作多）：用 \`threading\` 或 \`asyncio\`（IO 等待时释放 GIL）\r
\r
### 8.2 线程池与进程池\r
\r
\`\`\`python\r
from concurrent.futures import ThreadPoolExecutor, ProcessPoolExecutor\r
import time\r
\r
def fetch_url(url: str) -> str:\r
    """IO 密集型任务"""\r
    import urllib.request\r
    with urllib.request.urlopen(url) as resp:\r
        return resp.read().decode()\r
\r
# ===== 线程池（IO 密集型） =====\r
urls = ["https://example.com"] * 10\r
\r
with ThreadPoolExecutor(max_workers=5) as pool:\r
    # map：按顺序获取结果\r
    results = list(pool.map(fetch_url, urls))\r
    \r
    # submit：更灵活，返回 Future 对象\r
    futures = [pool.submit(fetch_url, url) for url in urls]\r
    results = [f.result() for f in futures]\r
\r
# ===== 进程池（CPU 密集型） =====\r
def cpu_heavy(n: int) -> int:\r
    return sum(i * i for i in range(n))\r
\r
with ProcessPoolExecutor() as pool:\r
    results = list(pool.map(cpu_heavy, [10**7] * 4))\r
\`\`\`\r
\r
### 8.3 asyncio 异步编程\r
\r
\`\`\`python\r
import asyncio\r
import aiohttp\r
\r
async def fetch(url: str) -> dict:\r
    """异步 HTTP 请求"""\r
    async with aiohttp.ClientSession() as session:\r
        async with session.get(url) as response:\r
            return await response.json()\r
\r
async def main():\r
    urls = [\r
        "https://api.example.com/data/1",\r
        "https://api.example.com/data/2",\r
        "https://api.example.com/data/3",\r
    ]\r
    # 并发执行所有请求（比串行快数倍）\r
    tasks = [fetch(url) for url in urls]\r
    results = await asyncio.gather(*tasks)\r
    \r
    # 带超时控制\r
    try:\r
        result = await asyncio.wait_for(fetch(urls[0]), timeout=5.0)\r
    except asyncio.TimeoutError:\r
        print("请求超时")\r
\r
# 运行\r
asyncio.run(main())\r
\`\`\`\r
\r
---\r
\r
## 九、标准库精选\r
\r
### 9.1 日期与时间\r
\r
\`\`\`python\r
from datetime import datetime, date, timedelta, timezone\r
\r
now = datetime.now()                          # 本地时间\r
utc_now = datetime.now(timezone.utc)          # UTC 时间\r
formatted = now.strftime("%Y-%m-%d %H:%M:%S") # 格式化输出\r
parsed = datetime.strptime("2024-01-15 10:30", "%Y-%m-%d %H:%M")  # 解析字符串\r
\r
# 时间差\r
tomorrow = now + timedelta(days=1)\r
diff = now - datetime(2024, 1, 1)\r
print(f"已过 {diff.days} 天 {diff.seconds // 3600} 小时")\r
\r
# 时区处理（Python 3.9+ zoneinfo）\r
from zoneinfo import ZoneInfo\r
tokyo = datetime.now(ZoneInfo("Asia/Tokyo"))\r
shanghai = datetime.now(ZoneInfo("Asia/Shanghai"))\r
\`\`\`\r
\r
### 9.2 正则表达式\r
\r
\`\`\`python\r
import re\r
\r
text = "联系方式：alice@example.com 或 bob@test.org，电话 13812345678"\r
\r
# 查找所有邮箱\r
emails = re.findall(r"[\\w.+-]+@[\\w-]+\\.[\\w.]+", text)\r
# ['alice@example.com', 'bob@test.org']\r
\r
# 命名分组（比数字索引可读性好得多）\r
pattern = re.compile(r"(?P<year>\\d{4})-(?P<month>\\d{2})-(?P<day>\\d{2})")\r
match = pattern.search("日期：2024-01-15")\r
if match:\r
    print(match.group("year"))    # "2024"\r
    print(match.groupdict())      # {'year': '2024', 'month': '01', 'day': '15'}\r
\r
# 替换\r
cleaned = re.sub(r"\\d+", "X", "订单号：12345，金额：999")\r
# '订单号：X，金额：X'\r
\r
# 编译正则（重复使用时性能更好）\r
email_pattern = re.compile(r"[\\w.+-]+@[\\w-]+\\.[\\w.]+")\r
matches = email_pattern.findall(text)\r
\`\`\`\r
\r
### 9.3 collections 容器工具\r
\r
\`\`\`python\r
from collections import Counter, defaultdict, deque, namedtuple, OrderedDict\r
\r
# Counter：计数器\r
words = ["apple", "banana", "apple", "cherry", "apple", "banana"]\r
counter = Counter(words)\r
# Counter({'apple': 3, 'banana': 2, 'cherry': 1})\r
counter.most_common(2)    # [('apple', 3), ('banana', 2)]\r
\r
# deque：双端队列（两端增删都是 O(1)）\r
dq = deque([1, 2, 3])\r
dq.appendleft(0)     # deque([0, 1, 2, 3])\r
dq.append(4)          # deque([0, 1, 2, 3, 4])\r
dq.popleft()          # 0\r
dq.pop()              # 4\r
dq.rotate(1)          # 右旋一位\r
\r
# 固定长度的 deque（自动丢弃旧元素）\r
recent = deque(maxlen=5)\r
for i in range(10):\r
    recent.append(i)\r
# deque([5, 6, 7, 8, 9], maxlen=5)\r
\`\`\`\r
\r
### 9.4 logging 日志\r
\r
\`\`\`python\r
import logging\r
\r
# 基础配置\r
logging.basicConfig(\r
    level=logging.INFO,\r
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",\r
    datefmt="%Y-%m-%d %H:%M:%S",\r
    handlers=[\r
        logging.FileHandler("app.log", encoding="utf-8"),\r
        logging.StreamHandler(),    # 同时输出到控制台\r
    ],\r
)\r
\r
logger = logging.getLogger(__name__)\r
\r
logger.debug("调试信息")\r
logger.info("服务启动成功，端口 %d", 8000)\r
logger.warning("磁盘使用率超过 80%%")\r
logger.error("数据库连接失败", exc_info=True)   # exc_info=True 附带堆栈\r
logger.critical("系统即将崩溃")\r
\`\`\`\r
\r
---\r
\r
## 十、Web 开发\r
\r
### 10.1 FastAPI（现代高性能，推荐）\r
\r
FastAPI 是目前最流行的 Python Web 框架，基于类型提示自动生成 API 文档，性能接近 Go/Node.js。\r
\r
\`\`\`python\r
# pip install fastapi uvicorn\r
from fastapi import FastAPI, HTTPException, Depends, Query\r
from pydantic import BaseModel, EmailStr, Field\r
from typing import Optional\r
\r
app = FastAPI(title="用户管理 API", version="1.0.0")\r
\r
# ===== 数据模型 =====\r
class UserCreate(BaseModel):\r
    name: str = Field(..., min_length=1, max_length=100, description="用户姓名")\r
    email: EmailStr\r
    age: Optional[int] = Field(None, ge=0, le=150)\r
\r
class UserResponse(BaseModel):\r
    id: int\r
    name: str\r
    email: str\r
    age: Optional[int]\r
\r
# ===== 路由 =====\r
@app.get("/api/users/{user_id}", response_model=UserResponse, tags=["用户"])\r
async def get_user(user_id: int):\r
    """根据 ID 获取用户详情"""\r
    # 模拟数据库查询\r
    user = {"id": user_id, "name": "Alice", "email": "a@b.com", "age": 25}\r
    if not user:\r
        raise HTTPException(status_code=404, detail="用户不存在")\r
    return user\r
\r
@app.post("/api/users", response_model=UserResponse, status_code=201, tags=["用户"])\r
async def create_user(user: UserCreate):\r
    """创建新用户"""\r
    return {"id": 1, **user.model_dump()}\r
\r
@app.get("/api/users", tags=["用户"])\r
async def list_users(\r
    page: int = Query(1, ge=1, description="页码"),\r
    size: int = Query(20, ge=1, le=100, description="每页数量"),\r
    keyword: Optional[str] = Query(None, description="搜索关键词"),\r
):\r
    """分页查询用户列表"""\r
    return {"page": page, "size": size, "items": []}\r
\r
# 启动：uvicorn main:app --reload --port 8000\r
# 文档：http://localhost:8000/docs（Swagger UI 自动生成）\r
# 备选文档：http://localhost:8000/redoc\r
\`\`\`\r
\r
### 10.2 Flask（轻量级）\r
\r
\`\`\`python\r
# pip install flask\r
from flask import Flask, request, jsonify, render_template\r
\r
app = Flask(__name__)\r
\r
@app.route("/")\r
def index():\r
    return render_template("index.html")\r
\r
@app.route("/api/users", methods=["GET"])\r
def get_users():\r
    page = request.args.get("page", 1, type=int)\r
    return jsonify({"users": [], "page": page})\r
\r
@app.route("/api/users", methods=["POST"])\r
def create_user():\r
    data = request.get_json()\r
    return jsonify({"id": 1, **data}), 201\r
\r
# 蓝图（模块化）\r
from flask import Blueprint\r
user_bp = Blueprint("users", __name__, url_prefix="/api")\r
\r
@user_bp.route("/users")\r
def list_users():\r
    return jsonify([])\r
\r
app.register_blueprint(user_bp)\r
\r
if __name__ == "__main__":\r
    app.run(debug=True, port=5000)\r
\`\`\`\r
\r
---\r
\r
## 十一、数据科学与 AI\r
\r
### 11.1 NumPy（数值计算）\r
\r
\`\`\`python\r
import numpy as np\r
\r
# 创建数组\r
arr = np.array([1, 2, 3, 4, 5])\r
matrix = np.zeros((3, 4))          # 3x4 零矩阵\r
random = np.random.randn(100, 50)  # 标准正态分布 100x50\r
\r
# 运算（向量化，比 for 循环快 100 倍+）\r
arr * 2             # 标量乘法 → [2, 4, 6, 8, 10]\r
arr @ arr           # 点积\r
np.dot(arr, arr)    # 同上\r
np.mean(arr)        # 均值\r
np.std(arr)         # 标准差\r
np.sum(arr, axis=0) # 按列求和\r
\r
# 广播机制（不同形状的数组自动对齐运算）\r
a = np.array([[1], [2], [3]])   # 形状 (3, 1)\r
b = np.array([10, 20, 30])      # 形状 (3,)\r
a + b                           # 形状 (3, 3) 自动广播\r
\`\`\`\r
\r
### 11.2 Pandas（数据分析）\r
\r
\`\`\`python\r
import pandas as pd\r
\r
# 创建 DataFrame\r
df = pd.DataFrame({\r
    "姓名": ["Alice", "Bob", "Charlie", "David"],\r
    "年龄": [25, 30, 35, 28],\r
    "城市": ["北京", "上海", "广州", "北京"],\r
    "薪资": [15000, 20000, 18000, 16000],\r
})\r
\r
# 查看\r
df.head(3)         # 前 3 行\r
df.info()          # 概要（类型、非空计数、内存）\r
df.describe()      # 统计汇总（均值、标准差、分位数）\r
\r
# 筛选\r
df[df["年龄"] > 28]\r
df[(df["年龄"] > 25) & (df["薪资"] > 16000)]\r
df[df["城市"].isin(["北京", "上海"])]\r
df[df["姓名"].str.startswith("A")]    # 字符串方法\r
\r
# 分组聚合\r
df.groupby("城市")["薪资"].agg(["mean", "max", "count"])\r
df.groupby("城市").agg(平均薪资=("薪资", "mean"), 人数=("姓名", "count"))\r
\r
# 排序\r
df.sort_values("薪资", ascending=False)\r
\r
# 新增/修改列\r
df["年薪"] = df["薪资"] * 12\r
df["年龄段"] = pd.cut(df["年龄"], bins=[0, 25, 30, 35, 100], labels=["青年", "中青年", "中年", "中老年"])\r
\r
# 读取/写入\r
df = pd.read_csv("data.csv", encoding="utf-8")\r
df = pd.read_excel("data.xlsx", sheet_name="Sheet1")\r
df.to_csv("output.csv", index=False, encoding="utf-8-sig")\r
\`\`\`\r
\r
### 11.3 机器学习（scikit-learn）\r
\r
\`\`\`python\r
from sklearn.model_selection import train_test_split, cross_val_score\r
from sklearn.ensemble import RandomForestClassifier, GradientBoostingClassifier\r
from sklearn.preprocessing import StandardScaler\r
from sklearn.metrics import classification_report, accuracy_score, confusion_matrix\r
import numpy as np\r
\r
# 1. 准备数据\r
X = np.random.randn(1000, 10)   # 1000 样本，10 特征\r
y = (X[:, 0] + X[:, 1] > 0).astype(int)  # 二分类标签\r
\r
# 2. 划分训练集/测试集（80% 训练，20% 测试）\r
X_train, X_test, y_train, y_test = train_test_split(\r
    X, y, test_size=0.2, random_state=42, stratify=y  # stratify 保持类别比例\r
)\r
\r
# 3. 特征标准化（均值为 0，标准差为 1）\r
scaler = StandardScaler()\r
X_train_scaled = scaler.fit_transform(X_train)    # 在训练集上 fit\r
X_test_scaled = scaler.transform(X_test)           # 在测试集上只 transform\r
\r
# 4. 训练模型\r
model = RandomForestClassifier(n_estimators=100, random_state=42, n_jobs=-1)\r
model.fit(X_train_scaled, y_train)\r
\r
# 5. 预测与评估\r
y_pred = model.predict(X_test_scaled)\r
print(f"准确率：{accuracy_score(y_test, y_pred):.4f}")\r
print(classification_report(y_test, y_pred))\r
print("混淆矩阵：\\n", confusion_matrix(y_test, y_pred))\r
\r
# 6. 交叉验证（更可靠的评估）\r
scores = cross_val_score(model, X_train_scaled, y_train, cv=5)\r
print(f"交叉验证准确率：{scores.mean():.4f} ± {scores.std():.4f}")\r
\r
# 7. 特征重要性\r
importances = model.feature_importances_\r
for i, imp in enumerate(sorted(enumerate(importances), key=lambda x: -x[1])):\r
    print(f"特征 {imp[0]}: {imp[1]:.4f}")\r
\`\`\`\r
\r
---\r
\r
## 十二、数据库操作\r
\r
### 12.1 SQLAlchemy ORM\r
\r
\`\`\`python\r
# pip install sqlalchemy\r
from sqlalchemy import create_engine, Column, Integer, String, DateTime, Text\r
from sqlalchemy.orm import declarative_base, sessionmaker, relationship\r
from datetime import datetime\r
\r
# 连接数据库\r
engine = create_engine("sqlite:///mydb.db", echo=False)\r
Base = declarative_base()\r
Session = sessionmaker(bind=engine)\r
\r
# 定义模型\r
class User(Base):\r
    __tablename__ = "users"\r
    \r
    id = Column(Integer, primary_key=True, autoincrement=True)\r
    name = Column(String(100), nullable=False, comment="用户姓名")\r
    email = Column(String(200), unique=True, comment="邮箱")\r
    created_at = Column(DateTime, default=datetime.now, comment="创建时间")\r
    \r
    # 关联（一对多）\r
    posts = relationship("Post", back_populates="author")\r
    \r
    def __repr__(self):\r
        return f"<User(id={self.id}, name={self.name!r})>"\r
\r
class Post(Base):\r
    __tablename__ = "posts"\r
    \r
    id = Column(Integer, primary_key=True)\r
    title = Column(String(200), nullable=False)\r
    content = Column(Text)\r
    author_id = Column(Integer, nullable=False)\r
    created_at = Column(DateTime, default=datetime.now)\r
    \r
    author = relationship("User", back_populates="posts")\r
\r
# 创建表\r
Base.metadata.create_all(engine)\r
\r
# 使用\r
session = Session()\r
\r
# 增\r
new_user = User(name="Alice", email="a@b.com")\r
session.add(new_user)\r
session.commit()\r
\r
# 查\r
user = session.query(User).filter_by(name="Alice").first()\r
users = session.query(User).filter(User.name.like("%Ali%")).all()\r
users = session.query(User).order_by(User.created_at.desc()).limit(10).all()\r
\r
# 改\r
user.email = "new_email@example.com"\r
session.commit()\r
\r
# 删\r
session.delete(user)\r
session.commit()\r
\r
session.close()\r
\`\`\`\r
\r
---\r
\r
## 十三、测试与调试\r
\r
### 13.1 pytest\r
\r
\`\`\`python\r
# pip install pytest pytest-cov\r
\r
# test_calculator.py\r
import pytest\r
\r
def add(a: float, b: float) -> float:\r
    return a + b\r
\r
def divide(a: float, b: float) -> float:\r
    if b == 0:\r
        raise ValueError("除数不能为零")\r
    return a / b\r
\r
# 基本测试\r
def test_add():\r
    assert add(1, 2) == 3\r
    assert add(-1, 1) == 0\r
    assert add(0.1, 0.2) == pytest.approx(0.3)    # 浮点数比较\r
\r
# 参数化测试（一组数据跑同一个测试）\r
@pytest.mark.parametrize("a, b, expected", [\r
    (1, 2, 3),\r
    (0, 0, 0),\r
    (-1, -1, -2),\r
    (100, 200, 300),\r
])\r
def test_add_parametrized(a, b, expected):\r
    assert add(a, b) == expected\r
\r
# 异常测试\r
def test_divide_by_zero():\r
    with pytest.raises(ValueError, match="除数不能为零"):\r
        divide(1, 0)\r
\r
# fixture（测试前的准备工作）\r
@pytest.fixture\r
def sample_users():\r
    return [{"name": "Alice", "age": 25}, {"name": "Bob", "age": 30}]\r
\r
def test_user_count(sample_users):\r
    assert len(sample_users) == 2\r
\r
# 运行：pytest -v --cov=my_module --cov-report=html\r
\`\`\`\r
\r
### 13.2 调试技巧\r
\r
\`\`\`python\r
# breakpoint()（Python 3.7+，推荐）\r
def buggy_function(data):\r
    breakpoint()   # 在此处暂停，进入 pdb 交互式调试\r
    result = process(data)\r
    return result\r
\r
# pdb 常用命令：\r
# n (next)      - 执行下一行\r
# s (step)      - 进入函数内部\r
# c (continue)  - 继续运行到下一个断点\r
# p variable    - 打印变量值\r
# l (list)      - 查看当前代码上下文\r
# q (quit)      - 退出调试\r
# h (help)      - 查看帮助\r
\r
# rich 库美化输出\r
from rich import print as rprint\r
from rich.traceback import install\r
install()   # 美化异常堆栈，高亮显示错误位置\r
rprint("[bold green]成功[/] 处理了 [cyan]{count}[/] 条数据")\r
\`\`\`\r
\r
---\r
\r
## 十四、工程化实践\r
\r
### 14.1 项目结构\r
\r
\`\`\`\r
my_project/\r
├── pyproject.toml          # 项目元数据与依赖（推荐替代 setup.py）\r
├── requirements.txt        # 锁定依赖版本\r
├── .env                    # 环境变量（不入版本控制）\r
├── .gitignore\r
├── README.md\r
├── src/\r
│   └── my_package/\r
│       ├── __init__.py\r
│       ├── main.py         # 应用入口\r
│       ├── config.py       # 配置管理\r
│       ├── models/         # 数据模型\r
│       ├── services/       # 业务逻辑\r
│       ├── api/            # 路由/控制器\r
│       └── utils/          # 工具函数\r
├── tests/\r
│   ├── conftest.py         # 共享 fixture\r
│   ├── test_models.py\r
│   └── test_services.py\r
├── scripts/                # 运维脚本\r
│   └── init_db.py\r
└── docker/\r
    ├── Dockerfile\r
    └── docker-compose.yml\r
\`\`\`\r
\r
### 14.2 配置管理\r
\r
\`\`\`python\r
# pip install pydantic-settings\r
from pydantic_settings import BaseSettings\r
\r
class Settings(BaseSettings):\r
    app_name: str = "MyApp"\r
    debug: bool = False\r
    database_url: str\r
    redis_url: str = "redis://localhost:6379"\r
    jwt_secret: str\r
    \r
    class Config:\r
        env_file = ".env"           # 从 .env 文件读取\r
        env_file_encoding = "utf-8"\r
\r
settings = Settings()\r
# 优先级：环境变量 > .env 文件 > 默认值\r
\`\`\`\r
\r
### 14.3 代码质量工具\r
\r
\`\`\`bash\r
# Ruff（超快的 linter + formatter，替代 flake8 + black + isort）\r
pip install ruff\r
ruff format .                    # 格式化代码\r
ruff check . --fix               # 检查并自动修复\r
\r
# 类型检查\r
pip install mypy\r
mypy src/                        # 静态类型检查\r
\r
# 预提交钩子（每次 git commit 前自动检查）\r
pip install pre-commit\r
# 在 .pre-commit-config.yaml 中配置后：\r
pre-commit install\r
\`\`\`\r
\r
### 14.4 Docker 部署\r
\r
\`\`\`dockerfile\r
# Dockerfile\r
FROM python:3.12-slim AS base\r
\r
WORKDIR /app\r
\r
# 先复制依赖文件（利用 Docker 缓存层）\r
COPY requirements.txt .\r
RUN pip install --no-cache-dir -r requirements.txt\r
\r
# 再复制源代码\r
COPY src/ ./src/\r
\r
EXPOSE 8000\r
\r
CMD ["uvicorn", "my_package.main:app", "--host", "0.0.0.0", "--port", "8000"]\r
\`\`\`\r
\r
\`\`\`yaml\r
# docker-compose.yml\r
version: "3.8"\r
services:\r
  api:\r
    build: .\r
    ports:\r
      - "8000:8000"\r
    environment:\r
      - DATABASE_URL=postgresql://user:pass@db:5432/mydb\r
      - REDIS_URL=redis://redis:6379\r
    depends_on:\r
      - db\r
      - redis\r
  \r
  db:\r
    image: postgres:16-alpine\r
    environment:\r
      POSTGRES_USER: user\r
      POSTGRES_PASSWORD: pass\r
      POSTGRES_DB: mydb\r
    volumes:\r
      - pgdata:/var/lib/postgresql/data\r
  \r
  redis:\r
    image: redis:7-alpine\r
\r
volumes:\r
  pgdata:\r
\`\`\`\r
\r
### 14.5 常用第三方库速查\r
\r
| 领域 | 库 | 用途 |\r
|------|-----|------|\r
| HTTP 请求 | \`httpx\`, \`requests\` | 同步/异步 HTTP 客户端 |\r
| CLI 工具 | \`click\`, \`typer\` | 命令行参数解析 |\r
| 数据校验 | \`pydantic\` | 数据模型与校验 |\r
| 任务队列 | \`celery\`, \`rq\` | 异步任务处理 |\r
| 缓存 | \`redis\`, \`cachetools\` | 缓存与速率限制 |\r
| 日志 | \`loguru\` | 更友好的日志库 |\r
| 进度条 | \`tqdm\`, \`rich\` | 循环进度显示 |\r
| 配置 | \`pydantic-settings\` | 环境变量管理 |\r
| 模板 | \`jinja2\` | HTML/文本模板 |\r
| 加密 | \`cryptography\` | 加密/解密/签名 |\r
| 图像 | \`Pillow\` | 图像处理 |\r
| PDF | \`reportlab\`, \`PyPDF2\` | PDF 生成与操作 |\r
| 爬虫 | \`scrapy\`, \`beautifulsoup4\` | 网页抓取 |\r
| 自动化 | \`selenium\`, \`playwright\` | 浏览器自动化 |\r
| ORM | \`sqlalchemy\` | 数据库 ORM |\r
| 异步框架 | \`fastapi\`, \`starlette\` | 高性能 Web |\r
| 全功能框架 | \`django\` | 企业级 Web |\r
| 轻量框架 | \`flask\` | 小型 Web 应用 |\r
| 数据科学 | \`numpy\`, \`pandas\` | 数据处理 |\r
| 可视化 | \`matplotlib\`, \`seaborn\`, \`plotly\` | 图表绘制 |\r
| 机器学习 | \`scikit-learn\` | 传统 ML |\r
| 深度学习 | \`pytorch\`, \`tensorflow\` | 神经网络 |\r
| 桌面应用 | \`PyQt6\`, \`customtkinter\` | GUI 应用 |\r
\r
---\r
\r
## 十五、学习资源与路线图\r
\r
### 15.1 学习路线图\r
\r
\`\`\`\r
入门（1-2 周）\r
├── Python 语法基础（变量、类型、运算符）\r
├── 数据结构（list/dict/tuple/set）\r
├── 控制流（if/for/while）\r
├── 函数定义与调用\r
└── 文件读写\r
\r
进阶（2-4 周）\r
├── 面向对象编程（类、继承、魔术方法）\r
├── 装饰器与生成器\r
├── 异常处理与上下文管理器\r
├── 标准库常用模块\r
├── 类型提示\r
└── 虚拟环境与包管理\r
\r
实战（4-8 周，选一个方向深入）\r
├── Web 开发：FastAPI / Django / Flask\r
├── 数据科学：NumPy / Pandas / Matplotlib\r
├── 自动化脚本：爬虫 / 办公自动化 / 系统管理\r
├── AI/ML：scikit-learn / PyTorch / TensorFlow\r
└── 桌面应用：PyQt / Tkinter / DearPyGui\r
\r
工程化（持续）\r
├── 测试（pytest）\r
├── 代码质量（ruff / mypy）\r
├── 容器化（Docker）\r
├── CI/CD（GitHub Actions）\r
├── 性能优化（cProfile / asyncio）\r
└── 日志与监控\r
\`\`\`\r
\r
### 15.2 推荐资源\r
\r
**官方文档**\r
- Python 官方文档（中文）：https://docs.python.org/zh-cn/3/\r
- Python 教程（官方）：https://docs.python.org/zh-cn/3/tutorial/\r
\r
**书籍**\r
- 《Python Crash Course》（Python 编程从入门到实践）— 入门首选\r
- 《Fluent Python》（流畅的 Python）— 进阶必读，深入理解 Python 特性\r
- 《Effective Python》— 90 条最佳实践\r
- 《Python Cookbook》— 经典食谱，按问题分类\r
\r
**在线平台**\r
- Real Python：https://realpython.com/ — 高质量教程和文章\r
- LeetCode：https://leetcode.cn/ — 算法练习\r
- Kaggle：https://www.kaggle.com/ — 数据科学实战\r
\r
**视频课程**\r
- Corey Schafer Python 教程（YouTube）— 讲解清晰\r
- 廖雪峰 Python 教程 — 中文入门经典\r
\r
### 15.3 常见陷阱\r
\r
\`\`\`python\r
# 1. 可变默认参数（最常见的坑！）\r
def append_to(element, target=[]):  # ❌ 所有调用共享同一个列表\r
    target.append(element)\r
    return target\r
\r
append_to(1)   # [1]\r
append_to(2)   # [1, 2]  ← 不是 [2]！\r
\r
def append_to(element, target=None):  # ✅ 每次创建新列表\r
    if target is None:\r
        target = []\r
    target.append(element)\r
    return target\r
\r
# 2. 浅拷贝陷阱\r
original = [[1, 2], [3, 4]]\r
shallow = original.copy()\r
shallow[0].append(99)   # original 也被修改！\r
\r
import copy\r
deep = copy.deepcopy(original)  # ✅ 深拷贝，完全独立\r
\r
# 3. 整数缓存（is vs ==）\r
a = 256\r
b = 256\r
a is b   # True（-5 到 256 被 Python 缓存）\r
\r
a = 257\r
b = 257\r
a is b   # 可能 False！永远用 == 比较值\r
\r
# 4. 闭包延迟绑定\r
funcs = [lambda: i for i in range(5)]\r
[f() for f in funcs]   # [4, 4, 4, 4, 4] 全是 4！\r
\r
funcs = [lambda i=i: i for i in range(5)]  # ✅ 默认参数立即绑定\r
[f() for f in funcs]   # [0, 1, 2, 3, 4]\r
\r
# 5. 修改遍历中的列表\r
nums = [1, 2, 3, 4, 5]\r
for n in nums:\r
    if n % 2 == 0:\r
        nums.remove(n)   # ❌ 跳过元素！\r
\r
# ✅ 正确做法：遍历副本或使用推导式\r
nums = [n for n in nums if n % 2 != 0]\r
\r
# 6. GIL 的误解\r
# CPU 密集型任务用 multiprocessing，不用 threading\r
# IO 密集型任务用 asyncio 或 threading\r
\`\`\`\r
\r
---\r
\r
## 十六、实战教程：从零搭建完整项目\r
\r
> 这一章手把手带你从零创建一个完整的 Python 项目，覆盖环境搭建、项目结构、代码编写、依赖管理、运行调试全流程。跟着做一遍就能上手。\r
\r
### 16.1 第一步：安装 Python\r
\r
**Windows 用户**\r
\r
1. 打开 https://www.python.org/downloads/\r
2. 下载最新稳定版（推荐 3.12.x）\r
3. **安装时务必勾选 "Add Python to PATH"**（这一步最关键！）\r
4. 安装完成后打开 PowerShell 验证：\r
\r
\`\`\`bash\r
python --version\r
# 输出：Python 3.12.x\r
\r
pip --version\r
# 输出：pip 24.x.x from ... (python 3.12)\r
\`\`\`\r
\r
> 如果提示 "python 不是内部命令"，说明安装时没勾选 Add to PATH。解决方法：重新运行安装程序 → 勾选 "Add to PATH" → 点 Modify → 完成。\r
\r
### 16.2 第二步：创建项目目录\r
\r
\`\`\`bash\r
# 创建项目文件夹\r
mkdir my_first_project\r
cd my_first_project\r
\r
# 用 VS Code 打开（推荐编辑器）\r
code .\r
\`\`\`\r
\r
### 16.3 第三步：创建虚拟环境\r
\r
虚拟环境是 Python 项目的标配，它让每个项目拥有独立的依赖包，互不干扰。\r
\r
\`\`\`bash\r
# 创建虚拟环境（在项目根目录执行）\r
python -m venv .venv\r
\r
# 激活虚拟环境\r
.venv\\Scripts\\activate       # Windows PowerShell\r
# source .venv/bin/activate  # Linux/Mac\r
\r
# 激活后命令行前面会出现 (.venv)\r
# 此时 pip install 的包都装在这个虚拟环境里\r
\`\`\`\r
\r
**VS Code 配置**（重要！）\r
\r
激活虚拟环境后，还需要让 VS Code 使用它：\r
1. \`Ctrl + Shift + P\` 打开命令面板\r
2. 输入 \`Python: Select Interpreter\`\r
3. 选择 \`.venv\` 中的 Python 解释器\r
\r
这样 VS Code 的代码提示、终端运行都会使用这个虚拟环境。\r
\r
### 16.4 第四步：搭建项目结构\r
\r
一个规范的 Python 项目结构如下：\r
\r
\`\`\`\r
my_first_project/\r
├── .venv/                  # 虚拟环境（不要提交到 Git）\r
├── .gitignore              # Git 忽略规则\r
├── requirements.txt        # 依赖列表\r
├── README.md               # 项目说明\r
├── main.py                 # 程序入口\r
├── config.py               # 配置文件\r
├── models/                 # 数据模型\r
│   └── __init__.py\r
├── services/               # 业务逻辑\r
│   └── __init__.py\r
├── utils/                  # 工具函数\r
│   └── __init__.py\r
└── tests/                  # 测试代码\r
    └── __init__.py\r
\`\`\`\r
\r
**创建这些文件和目录：**\r
\r
\`\`\`bash\r
# Windows PowerShell\r
mkdir models, services, utils, tests\r
New-Item -ItemType File -Path "models/__init__.py" -Force\r
New-Item -ItemType File -Path "services/__init__.py" -Force\r
New-Item -ItemType File -Path "utils/__init__.py" -Force\r
New-Item -ItemType File -Path "tests/__init__.py" -Force\r
New-Item -ItemType File -Path "main.py" -Force\r
New-Item -ItemType File -Path "config.py" -Force\r
New-Item -ItemType File -Path "requirements.txt" -Force\r
New-Item -ItemType File -Path "README.md" -Force\r
New-Item -ItemType File -Path ".gitignore" -Force\r
\`\`\`\r
\r
> **\`__init__.py\` 是什么？** 它是一个空文件，告诉 Python "这个目录是一个包（package），可以被 import"。内容可以为空，但文件必须存在。\r
\r
### 16.5 第五步：编写第一个程序\r
\r
让我们写一个简单的**待办事项管理器**作为练手项目。\r
\r
**\`config.py\` —— 配置文件**\r
\r
\`\`\`python\r
# 配置文件：集中管理项目配置\r
APP_NAME = "待办事项管理器"\r
VERSION = "1.0.0"\r
DATA_FILE = "todos.json"\r
\`\`\`\r
\r
**\`models/__init__.py\` —— 数据模型**\r
\r
\`\`\`python\r
"""数据模型：定义待办事项的数据结构"""\r
from dataclasses import dataclass, field\r
from datetime import datetime\r
from typing import Optional\r
\r
\r
@dataclass\r
class Todo:\r
    """待办事项"""\r
    title: str\r
    id: int\r
    completed: bool = False\r
    priority: str = "普通"    # 高 / 普通 / 低\r
    created_at: str = field(default_factory=lambda: datetime.now().strftime("%Y-%m-%d %H:%M:%S"))\r
    description: Optional[str] = None\r
\r
    def to_dict(self) -> dict:\r
        """转换为字典（用于 JSON 序列化）"""\r
        return {\r
            "id": self.id,\r
            "title": self.title,\r
            "completed": self.completed,\r
            "priority": self.priority,\r
            "created_at": self.created_at,\r
            "description": self.description,\r
        }\r
\r
    @classmethod\r
    def from_dict(cls, data: dict) -> "Todo":\r
        """从字典创建（用于 JSON 反序列化）"""\r
        return cls(**data)\r
\`\`\`\r
\r
**\`services/__init__.py\` —— 业务逻辑**\r
\r
\`\`\`python\r
"""业务逻辑：管理待办事项的增删改查"""\r
import json\r
from pathlib import Path\r
from typing import Optional\r
from models import Todo\r
\r
\r
class TodoService:\r
    """待办事项服务"""\r
\r
    def __init__(self, data_file: str = "todos.json"):\r
        self.data_file = Path(data_file)\r
        self.todos: list[Todo] = []\r
        self._next_id = 1\r
        self.load()\r
\r
    def load(self):\r
        """从文件加载数据"""\r
        if self.data_file.exists():\r
            with open(self.data_file, "r", encoding="utf-8") as f:\r
                data = json.load(f)\r
                self.todos = [Todo.from_dict(item) for item in data]\r
                self._next_id = max((t.id for t in self.todos), default=0) + 1\r
\r
    def save(self):\r
        """保存数据到文件"""\r
        with open(self.data_file, "w", encoding="utf-8") as f:\r
            json.dump([t.to_dict() for t in self.todos], f, ensure_ascii=False, indent=2)\r
\r
    def add(self, title: str, priority: str = "普通", description: str = None) -> Todo:\r
        """添加待办事项"""\r
        todo = Todo(title=title, id=self._next_id, priority=priority, description=description)\r
        self.todos.append(todo)\r
        self._next_id += 1\r
        self.save()\r
        return todo\r
\r
    def delete(self, todo_id: int) -> bool:\r
        """删除待办事项"""\r
        for i, todo in enumerate(self.todos):\r
            if todo.id == todo_id:\r
                self.todos.pop(i)\r
                self.save()\r
                return True\r
        return False\r
\r
    def complete(self, todo_id: int) -> bool:\r
        """标记为已完成"""\r
        for todo in self.todos:\r
            if todo.id == todo_id:\r
                todo.completed = True\r
                self.save()\r
                return True\r
        return False\r
\r
    def list_all(self, show_completed: bool = True) -> list[Todo]:\r
        """列出所有待办事项"""\r
        if show_completed:\r
            return self.todos\r
        return [t for t in self.todos if not t.completed]\r
\r
    def find(self, keyword: str) -> list[Todo]:\r
        """按关键词搜索"""\r
        return [t for t in self.todos if keyword in t.title]\r
\`\`\`\r
\r
**\`utils/__init__.py\` —— 工具函数**\r
\r
\`\`\`python\r
"""工具函数：终端界面美化"""\r
\r
# 颜色代码（终端 ANSI 转义序列）\r
class Color:\r
    GREEN = "\\033[92m"\r
    RED = "\\033[91m"\r
    YELLOW = "\\033[93m"\r
    BLUE = "\\033[94m"\r
    GRAY = "\\033[90m"\r
    BOLD = "\\033[1m"\r
    RESET = "\\033[0m"\r
\r
\r
def print_header(title: str):\r
    """打印标题"""\r
    width = 50\r
    print(f"\\n{Color.BOLD}{'=' * width}")\r
    print(f"  {title}")\r
    print(f"{'=' * width}{Color.RESET}\\n")\r
\r
\r
def print_todo(todo, index: int = 0):\r
    """格式化打印单条待办"""\r
    status = f"{Color.GREEN}✓{Color.RESET}" if todo.completed else f"{Color.RED}○{Color.RESET}"\r
    priority_color = {\r
        "高": Color.RED,\r
        "普通": Color.YELLOW,\r
        "低": Color.GRAY,\r
    }.get(todo.priority, Color.RESET)\r
\r
    print(f"  {status} [{priority_color}{todo.priority}{Color.RESET}] "\r
          f"{Color.BOLD}#{todo.id}{Color.RESET} {todo.title}")\r
    if todo.description:\r
        print(f"    {Color.GRAY}└─ {todo.description}{Color.RESET}")\r
\r
\r
def print_todos(todos: list, title: str = "待办列表"):\r
    """打印待办列表"""\r
    print_header(title)\r
    if not todos:\r
        print(f"  {Color.GRAY}（空）{Color.RESET}")\r
        return\r
    for i, todo in enumerate(todos, 1):\r
        print_todo(todo, i)\r
    print(f"\\n  共 {len(todos)} 条")\r
\`\`\`\r
\r
**\`main.py\` —— 程序入口**\r
\r
\`\`\`python\r
"""待办事项管理器 —— 程序入口"""\r
import sys\r
from config import APP_NAME, VERSION\r
from services import TodoService\r
from utils import print_header, print_todos, Color\r
\r
\r
def show_menu():\r
    """显示操作菜单"""\r
    print(f"\\n{Color.BLUE}请选择操作：{Color.RESET}")\r
    print("  1. 查看所有待办")\r
    print("  2. 添加新待办")\r
    print("  3. 标记为完成")\r
    print("  4. 删除待办")\r
    print("  5. 搜索待办")\r
    print("  0. 退出程序")\r
\r
\r
def main():\r
    """主循环"""\r
    service = TodoService()\r
    print_header(f"{APP_NAME} v{VERSION}")\r
\r
    while True:\r
        show_menu()\r
        choice = input(f"\\n{Color.BOLD}请输入选项编号：{Color.RESET}").strip()\r
\r
        if choice == "1":\r
            todos = service.list_all()\r
            print_todos(todos)\r
\r
        elif choice == "2":\r
            title = input("待办标题：").strip()\r
            if not title:\r
                print(f"{Color.RED}标题不能为空！{Color.RESET}")\r
                continue\r
            priority = input("优先级（高/普通/低，默认普通）：").strip() or "普通"\r
            desc = input("描述（可选，回车跳过）：").strip() or None\r
            todo = service.add(title, priority, desc)\r
            print(f"{Color.GREEN}✓ 已添加：#{todo.id} {todo.title}{Color.RESET}")\r
\r
        elif choice == "3":\r
            todo_id = int(input("待办编号：").strip())\r
            if service.complete(todo_id):\r
                print(f"{Color.GREEN}✓ 已完成！{Color.RESET}")\r
            else:\r
                print(f"{Color.RED}未找到编号 {todo_id} 的待办{Color.RESET}")\r
\r
        elif choice == "4":\r
            todo_id = int(input("待办编号：").strip())\r
            if service.delete(todo_id):\r
                print(f"{Color.GREEN}✓ 已删除！{Color.RESET}")\r
            else:\r
                print(f"{Color.RED}未找到编号 {todo_id} 的待办{Color.RESET}")\r
\r
        elif choice == "5":\r
            keyword = input("搜索关键词：").strip()\r
            results = service.find(keyword)\r
            print_todos(results, f"搜索结果：{keyword}")\r
\r
        elif choice == "0":\r
            print(f"\\n{Color.GREEN}再见！{Color.RESET}\\n")\r
            sys.exit(0)\r
\r
        else:\r
            print(f"{Color.RED}无效选项，请重新输入{Color.RESET}")\r
\r
\r
if __name__ == "__main__":\r
    main()\r
\`\`\`\r
\r
### 16.6 第六步：运行程序\r
\r
\`\`\`bash\r
# 确保虚拟环境已激活（命令行前面有 (.venv)）\r
# 在项目根目录执行：\r
python main.py\r
\`\`\`\r
\r
你会看到这样的界面：\r
\r
\`\`\`\r
==================================================\r
  待办事项管理器 v1.0.0\r
==================================================\r
\r
请选择操作：\r
  1. 查看所有待办\r
  2. 添加新待办\r
  3. 标记为完成\r
  4. 删除待办\r
  5. 搜索待办\r
  0. 退出程序\r
\r
请输入选项编号：\r
\`\`\`\r
\r
### 16.7 第七步：添加第三方依赖\r
\r
让我们给项目加上 \`rich\` 库，让终端输出更漂亮。\r
\r
\`\`\`bash\r
# 安装 rich 库\r
pip install rich\r
\r
# 导出依赖到 requirements.txt\r
pip freeze > requirements.txt\r
\r
# 查看 requirements.txt 内容\r
cat requirements.txt\r
# rich==13.7.0\r
# markdown-it-py==3.0.0\r
# ...\r
\`\`\`\r
\r
**\`requirements.txt\` 的内容示例：**\r
\r
\`\`\`\r
rich==13.7.0\r
\`\`\`\r
\r
> **为什么需要 requirements.txt？** 别人拿到你的项目后，只需执行 \`pip install -r requirements.txt\` 就能安装所有依赖，不需要手动一个个装。\r
\r
### 16.8 第八步：用 rich 美化输出\r
\r
修改 \`utils/__init__.py\`，用 \`rich\` 替代手动 ANSI 颜色代码：\r
\r
\`\`\`python\r
"""工具函数：使用 rich 库美化终端输出"""\r
from rich.console import Console\r
from rich.table import Table\r
from rich.panel import Panel\r
\r
console = Console()\r
\r
\r
def print_header(title: str):\r
    """打印漂亮的标题"""\r
    console.print(Panel(title, style="bold blue", expand=False))\r
\r
\r
def print_todos(todos: list, title: str = "待办列表"):\r
    """用表格打印待办列表"""\r
    print_header(title)\r
\r
    if not todos:\r
        console.print("  [dim]（空）[/dim]")\r
        return\r
\r
    table = Table(show_header=True, header_style="bold cyan")\r
    table.add_column("状态", width=4, justify="center")\r
    table.add_column("ID", width=5, justify="right")\r
    table.add_column("优先级", width=6)\r
    table.add_column("标题")\r
    table.add_column("描述", style="dim")\r
\r
    for todo in todos:\r
        status = "[green]✓[/green]" if todo.completed else "[red]○[/red]"\r
        priority = {\r
            "高": "[red]高[/red]",\r
            "普通": "[yellow]普通[/yellow]",\r
            "低": "[dim]低[/dim]",\r
        }.get(todo.priority, todo.priority)\r
        table.add_row(status, str(todo.id), priority, todo.title, todo.description or "")\r
\r
    console.print(table)\r
    console.print(f"\\n  共 [bold]{len(todos)}[/bold] 条")\r
\`\`\`\r
\r
### 16.9 第九步：添加 \`.gitignore\`\r
\r
\`\`\`gitignore\r
# Python\r
__pycache__/\r
*.py[cod]\r
*.egg-info/\r
dist/\r
build/\r
\r
# 虚拟环境\r
.venv/\r
venv/\r
env/\r
\r
# IDE\r
.vscode/\r
.idea/\r
*.swp\r
*.swo\r
\r
# 数据文件（不提交到 Git）\r
todos.json\r
\r
# 环境变量\r
.env\r
\`\`\`\r
\r
> **为什么要 \`.gitignore\`？** 防止虚拟环境、缓存文件、敏感配置等被提交到 Git 仓库。\r
\r
### 16.10 第十步：写 README\r
\r
\`\`\`markdown\r
# 待办事项管理器\r
\r
一个基于终端的待办事项管理工具，支持增删改查、优先级标记、数据持久化。\r
\r
## 功能特性\r
\r
- ✅ 添加/删除/完成待办事项\r
- 🔍 关键词搜索\r
- 🎯 优先级标记（高/普通/低）\r
- 💾 数据自动保存到 JSON 文件\r
- 🎨 彩色终端输出（基于 rich）\r
\r
## 快速开始\r
\r
### 1. 安装依赖\r
\r
\`\`\`bash\r
python -m venv .venv\r
.venv\\Scripts\\activate   # Windows\r
pip install -r requirements.txt\r
\`\`\`\r
\r
### 2. 运行\r
\r
\`\`\`bash\r
python main.py\r
\`\`\`\r
\r
## 项目结构\r
\r
\`\`\`\r
my_first_project/\r
├── main.py          # 程序入口\r
├── config.py        # 配置文件\r
├── models/          # 数据模型\r
├── services/        # 业务逻辑\r
├── utils/           # 工具函数\r
└── tests/           # 测试代码\r
\`\`\`\r
\`\`\`\r
\r
### 16.11 完整项目总结\r
\r
经过以上步骤，你拥有了一个完整的 Python 项目：\r
\r
\`\`\`\r
my_first_project/\r
├── .venv/              # 虚拟环境\r
├── .gitignore          # Git 忽略规则\r
├── requirements.txt    # 依赖清单\r
├── README.md           # 项目说明\r
├── main.py             # 程序入口（用户交互界面）\r
├── config.py           # 配置管理\r
├── models/\r
│   └── __init__.py     # Todo 数据模型（dataclass）\r
├── services/\r
│   └── __init__.py     # TodoService 业务逻辑（增删改查 + JSON 持久化）\r
├── utils/\r
│   └── __init__.py     # 终端美化输出（rich）\r
└── tests/\r
    └── __init__.py     # 测试代码（待补充）\r
\`\`\`\r
\r
**这个项目用到了哪些知识点？**\r
\r
| 知识点 | 用在哪里 |\r
|--------|----------|\r
| dataclass | Todo 数据模型定义 |\r
| 类型提示 | 函数参数和返回值标注 |\r
| JSON 文件读写 | 数据持久化 |\r
| pathlib | 文件路径操作 |\r
| 列表推导式 | 过滤未完成事项 |\r
| 类与方法 | TodoService 封装业务逻辑 |\r
| 异常处理 | 输入验证 |\r
| 第三方库 | rich 美化输出 |\r
| 虚拟环境 | 依赖隔离 |\r
| \`__name__ == "__main__"\` | 程序入口判断 |\r
\r
### 16.12 下一步：给项目加测试\r
\r
\`\`\`python\r
# tests/test_services.py\r
import pytest\r
from pathlib import Path\r
from services import TodoService\r
\r
\r
@pytest.fixture\r
def service(tmp_path):\r
    """创建测试用的 TodoService（使用临时目录）"""\r
    data_file = tmp_path / "test_todos.json"\r
    return TodoService(data_file=str(data_file))\r
\r
\r
def test_add_todo(service):\r
    """测试添加待办"""\r
    todo = service.add("买牛奶", priority="高")\r
    assert todo.title == "买牛奶"\r
    assert todo.priority == "高"\r
    assert todo.id == 1\r
    assert not todo.completed\r
\r
\r
def test_complete_todo(service):\r
    """测试标记完成"""\r
    todo = service.add("写报告")\r
    assert service.complete(todo.id)\r
    assert service.todos[0].completed\r
\r
\r
def test_delete_todo(service):\r
    """测试删除"""\r
    todo = service.add("过期任务")\r
    assert service.delete(todo.id)\r
    assert len(service.todos) == 0\r
\r
\r
def test_find_todo(service):\r
    """测试搜索"""\r
    service.add("买牛奶")\r
    service.add("买面包")\r
    service.add("写报告")\r
    results = service.find("买")\r
    assert len(results) == 2\r
\r
\r
def test_persistence(tmp_path):\r
    """测试数据持久化"""\r
    data_file = tmp_path / "test.json"\r
    \r
    # 创建并添加数据\r
    service1 = TodoService(data_file=str(data_file))\r
    service1.add("持久化测试")\r
    \r
    # 重新加载\r
    service2 = TodoService(data_file=str(data_file))\r
    assert len(service2.todos) == 1\r
    assert service2.todos[0].title == "持久化测试"\r
\`\`\`\r
\r
**运行测试：**\r
\r
\`\`\`bash\r
pip install pytest\r
pytest tests/ -v\r
\`\`\`\r
\r
输出：\r
\r
\`\`\`\r
tests/test_services.py::test_add_todo PASSED\r
tests/test_services.py::test_complete_todo PASSED\r
tests/test_services.py::test_delete_todo PASSED\r
tests/test_services.py::test_find_todo PASSED\r
tests/test_services.py::test_persistence PASSED\r
\r
5 passed in 0.12s\r
\`\`\`\r
\r
### 16.13 更多练手项目推荐\r
\r
| 难度 | 项目 | 涉及知识点 |\r
|------|------|------------|\r
| ⭐ | 猜数字游戏 | 随机数、循环、条件判断、输入输出 |\r
| ⭐⭐ | 密码生成器 | 字符串操作、random 模块、命令行参数 |\r
| ⭐⭐ | 文件批量重命名 | pathlib、正则、os 操作 |\r
| ⭐⭐⭐ | 天气查询 CLI | requests、JSON API 调用、异常处理 |\r
| ⭐⭐⭐ | Markdown 转 HTML | 文件读写、字符串处理、正则 |\r
| ⭐⭐⭐⭐ | 个人记账本 | SQLite 数据库、dataclass、终端 UI |\r
| ⭐⭐⭐⭐ | 网页爬虫 | requests、BeautifulSoup、数据提取 |\r
| ⭐⭐⭐⭐⭐ | REST API 服务 | FastAPI、Pydantic、数据库 CRUD |\r
| ⭐⭐⭐⭐⭐ | 桌面计算器 | PyQt6/Tkinter、事件驱动、GUI 布局 |\r
\r
---\r
\r
> 本知识库持续更新，覆盖 Python 生态核心内容。建议按路线图循序渐进，结合实战项目加深理解。每个知识点都动手敲一遍，比看十遍教程都管用。\r
`,Ts={class:"knowledge-page"},Rs={class:"top-bar"},As={class:"top-bar-left"},Ps={class:"top-bar-center"},Cs={key:0,class:"search-dropdown"},Ns=["onMousedown"],Os={class:"search-item-title"},Is={key:0,class:"search-item-context"},Ms={key:1,class:"search-dropdown search-empty"},Ls={class:"top-bar-right"},Ds={class:"font-size-label"},$s={class:"progress-label"},Bs={class:"progress-bar"},zs={class:"knowledge-body"},qs={class:"toc-sidebar"},Fs={class:"toc-title"},Hs={class:"toc-count"},js={class:"toc-list"},Us=["onClick"],Gs=["onClick"],Ws=["innerHTML"],Ks=pt({__name:"PythonKnowledgeView",setup(n){te.registerLanguage("python",Yn),te.registerLanguage("python3",Yn),te.registerLanguage("bash",Jn),te.registerLanguage("shell",Jn),te.registerLanguage("sql",Es),te.registerLanguage("dockerfile",er),te.registerLanguage("yaml",vs),te.registerLanguage("dockerfile",er),te.registerLanguage("plaintext",nr),te.registerLanguage("text",nr);const e=new M.Renderer;e.code=function(P){const b=P.text||"",C=P.lang||"text";let $;try{$=te.getLanguage(C)?te.highlight(b,{language:C}).value:te.highlight(b,{language:"python"}).value}catch{$=b.replace(/</g,"&lt;").replace(/>/g,"&gt;")}return`<div class="code-block">
    <div class="code-header">
      <span class="code-lang">${C}</span>
      <button class="code-copy-btn" onclick="(function(btn){var pre=btn.closest('.code-block').querySelector('code');navigator.clipboard.writeText(pre.innerText).then(function(){btn.textContent='已复制';btn.classList.add('copied');setTimeout(function(){btn.textContent='复制';btn.classList.remove('copied')},1500)})})(this)">复制</button>
    </div>
    <pre><code class="hljs language-${C}">${$}</code></pre>
  </div>`},e.heading=function(P){const b=typeof P.text=="string"?P.text:String(P.text??""),C=P.depth??2,$=b.replace(/<[^>]*>/g,"").replace(/\s+/g,"-").replace(/[^\w\u4e00-\u9fff-]/g,"").toLowerCase();return`<h${C} id="${$}">${b}<a class="heading-anchor" data-anchor="${$}">#</a></h${C}>`},e.link=function(P){const b=P.href||"",C=P.text||"",$=P.title?` title="${P.title}"`:"";return b.startsWith("http://")||b.startsWith("https://")?`<a href="${b}" target="_blank" rel="noopener noreferrer"${$}>${C}</a>`:b.startsWith("#")?`<a href="javascript:void(0)" data-anchor="${b.slice(1)}"${$}>${C}</a>`:`<a href="${b}"${$}>${C}</a>`},M.setOptions({renderer:e,gfm:!0,breaks:!1});const r=_t(()=>M.parse(Ss)),t=we([]),s=we("");function o(){const P=document.querySelector(".knowledge-content");if(!P)return;const b=P.querySelectorAll("h2, h3"),C=[];let $=null;b.forEach(F=>{var ye,H;const q=F.id||((ye=F.textContent)==null?void 0:ye.replace(/\s+/g,"-").replace(/[^\w\u4e00-\u9fff-]/g,"").toLowerCase())||"";F.id=q;const Y={id:q,text:((H=F.textContent)==null?void 0:H.replace("#","").trim())||"",level:parseInt(F.tagName[1]),children:[]};F.tagName==="H2"?(C.push(Y),$=Y):F.tagName==="H3"&&$&&$.children.push(Y)}),t.value=C}const l=we(0),c=we(!1);let a=null;function f(){if(!a)return;const{scrollTop:P,scrollHeight:b,clientHeight:C}=a;l.value=b>C?Math.round(P/(b-C)*100):0,c.value=P>300;const $=a.querySelectorAll("h2[id], h3[id]");let F="";for(const q of $)q.getBoundingClientRect().top<=80&&(F=q.id);s.value=F}function u(P){if(!a)return;const b=a.querySelector(`#${CSS.escape(P)}`);b&&a.scrollTo({top:b.offsetTop-16,behavior:"smooth"})}function x(){a==null||a.scrollTo({top:0,behavior:"smooth"})}function d(P){const b=P.target.closest("a[data-anchor]");if(!b)return;P.preventDefault();const C=b.dataset.anchor;C&&u(C)}const v=we(""),m=we([]),A=we(!1);function j(){const P=v.value.trim().toLowerCase();if(!P){m.value=[];return}const b=document.querySelector(".knowledge-content");if(!b)return;const C=b.querySelectorAll("h2[id], h3[id]"),$=[];C.forEach(F=>{var H;let q="",Y=F.nextElementSibling;for(;Y&&!["H1","H2","H3"].includes(Y.tagName);)q+=Y.textContent+" ",Y=Y.nextElementSibling;if((F.textContent+" "+q).toLowerCase().includes(P)){const se=q.toLowerCase().indexOf(P);let ge="";if(se>=0){const ze=Math.max(0,se-30),qe=Math.min(q.length,se+P.length+50);ge=(ze>0?"...":"")+q.slice(ze,qe).trim()+(qe<q.length?"...":"")}$.push({id:F.id,text:((H=F.textContent)==null?void 0:H.replace("#","").trim())||"",context:ge})}}),m.value=$}ut(v,()=>{j()});function G(){setTimeout(()=>{A.value=!1},200)}const ne=we(15);function ue(P){ne.value=Math.max(12,Math.min(20,ne.value+P))}return dt(async()=>{await ft(),a=document.querySelector(".knowledge-content"),a&&a.addEventListener("scroll",f,{passive:!0}),o(),f()}),ht(()=>{a&&a.removeEventListener("scroll",f)}),(P,b)=>{const C=mt,$=bt,F=yt,q=kt,Y=xt,ye=wt;return le(),ae("div",Ts,[U("div",Rs,[U("div",As,[oe(C,{class:"top-bar-icon"},{default:me(()=>[...b[4]||(b[4]=[U("svg",{viewBox:"0 0 24 24",fill:"currentColor"},[U("path",{d:"M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"})],-1)])]),_:1}),b[6]||(b[6]=U("span",{class:"top-bar-title"},"Python 知识库",-1)),oe($,{size:"small",type:"info"},{default:me(()=>[...b[5]||(b[5]=[dn("Markdown",-1)])]),_:1})]),U("div",Ps,[oe(F,{modelValue:v.value,"onUpdate:modelValue":b[0]||(b[0]=H=>v.value=H),placeholder:"搜索知识点...",clearable:"","prefix-icon":"Search",style:{width:"280px"},onFocus:b[1]||(b[1]=H=>A.value=!0),onBlur:G},null,8,["modelValue"]),A.value&&m.value.length>0?(le(),ae("div",Cs,[(le(!0),ae(Ze,null,un(m.value,H=>(le(),ae("div",{key:H.id,class:"search-item",onMousedown:fn(se=>{u(H.id),A.value=!1},["prevent"])},[U("div",Os,Te(H.text),1),H.context?(le(),ae("div",Is,Te(H.context),1)):Xe("",!0)],40,Ns))),128))])):Xe("",!0),A.value&&v.value&&m.value.length===0?(le(),ae("div",Ms," 未找到相关内容 ")):Xe("",!0)]),U("div",Ls,[oe(Y,{content:"缩小字号",placement:"bottom"},{default:me(()=>[oe(q,{size:"small",text:"",onClick:b[2]||(b[2]=H=>ue(-1))},{default:me(()=>[...b[7]||(b[7]=[dn("A-",-1)])]),_:1})]),_:1}),U("span",Ds,Te(ne.value)+"px",1),oe(Y,{content:"放大字号",placement:"bottom"},{default:me(()=>[oe(q,{size:"small",text:"",onClick:b[3]||(b[3]=H=>ue(1))},{default:me(()=>[...b[8]||(b[8]=[dn("A+",-1)])]),_:1})]),_:1}),oe(ye,{direction:"vertical"}),oe(Y,{content:"阅读进度",placement:"bottom"},{default:me(()=>[U("span",$s,Te(l.value)+"%",1)]),_:1})])]),U("div",Bs,[U("div",{class:"progress-bar-fill",style:Hn({width:l.value+"%"})},null,4)]),U("div",zs,[U("aside",qs,[U("div",Fs,[b[9]||(b[9]=U("span",null,"目录导航",-1)),U("span",Hs,Te(t.value.length)+" 章",1)]),U("nav",js,[(le(!0),ae(Ze,null,un(t.value,H=>(le(),ae(Ze,{key:H.id},[U("a",{class:jn(["toc-item toc-h2",{active:s.value===H.id}]),onClick:fn(se=>u(H.id),["prevent"])},Te(H.text),11,Us),(le(!0),ae(Ze,null,un(H.children,se=>(le(),ae("a",{key:se.id,class:jn(["toc-item toc-h3",{active:s.value===se.id}]),onClick:fn(ge=>u(se.id),["prevent"])},Te(se.text),11,Gs))),128))],64))),128))])]),U("main",{class:"knowledge-content",style:Hn({fontSize:ne.value+"px"}),innerHTML:r.value,onClick:d},null,12,Ws)]),oe(gt,{name:"fade"},{default:me(()=>[c.value?(le(),ae("div",{key:0,class:"back-top",onClick:x},[oe(C,{size:20},{default:me(()=>[...b[10]||(b[10]=[U("svg",{viewBox:"0 0 24 24",fill:"currentColor"},[U("path",{d:"M7.41 15.41L12 10.83l4.59 4.58L18 14l-6-6-6 6z"})],-1)])]),_:1})])):Xe("",!0)]),_:1})])}}}),Ys=Et(Ks,[["__scopeId","data-v-80fdca3b"]]);export{Ys as default};
