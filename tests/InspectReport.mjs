// Read-only visual check of the application's actual XLSX output.
import fs from 'node:fs/promises';
import path from 'node:path';
import {FileBlob, SpreadsheetFile} from '@oai/artifact-tool';
const file = process.argv[2];
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(file));
console.log((await workbook.inspect({kind:'sheet',include:'id,name'})).ndjson);
console.log((await workbook.inspect({kind:'drawing',maxChars:1500})).ndjson);
const sheet = workbook.worksheets.getItemAt(0);
console.log((await workbook.inspect({kind:'table',range:`'${sheet.name}'!D2:Y8`,include:'values',tableMaxRows:7,tableMaxCols:25,maxChars:7000})).ndjson);
console.log((await workbook.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:20}})).ndjson);
const preview = await workbook.render({sheetName:sheet.name,range:'A2:H9',scale:1.5,format:'png'});
await fs.writeFile(path.join(path.dirname(file),path.basename(file,'.xlsx')+'-render.png'),new Uint8Array(await preview.arrayBuffer()));
