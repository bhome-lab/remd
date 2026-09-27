namespace RemoteControl.Infrastructure.Execution;

internal static class ScriptBootstraps
{
    public const string Python = """
        import sys, os, json, traceback
        control = open(os.environ['REMOTE_CONTROL_SESSION_PIPE'], 'r+b', buffering=0)
        namespace = {'__name__': '__main__'}
        while True:
            line = control.readline()
            if not line:
                break
            request = json.loads(line)
            reply = {'id': request['id'], 'ok': True, 'error': None}
            try:
                exec(compile(request['code'], '<remote>', 'exec'), namespace, namespace)
            except Exception as error:
                reply['ok'] = False
                reply['error'] = str(error)
                traceback.print_exc()
            finally:
                sys.stdout.flush()
                sys.stderr.flush()
                marker = ('\x1eREMOTE_CONTROL_END_' + request['id'] + '\x1f').encode('utf-8')
                os.write(1, marker)
                os.write(2, marker)
                control.write((json.dumps(reply) + '\n').encode('utf-8'))
        """;

    public const string Node = """
        const net = require('net'), readline = require('readline'), repl = require('repl'), stream = require('stream');
        const evaluationDomain = require('domain').create();
        const input = new stream.PassThrough();
        const output = new stream.Writable({write(chunk, encoding, callback) { callback(); }});
        const shell = new repl.REPLServer({prompt: '', terminal: false, useGlobal: true, input, output, domain: evaluationDomain});
        shell.context.require = require;
        shell.context.process = process;
        shell.context.Buffer = Buffer;
        shell.context.console = console;
        const control = net.connect(process.env.REMOTE_CONTROL_SESSION_PIPE);
        const lines = readline.createInterface({input: control, crlfDelay: Infinity});
        let chain = Promise.resolve();
        function execute(request) {
          return new Promise(resolve => {
            let settled = false;
            async function complete(error, value) {
              if (settled) return;
              settled = true;
              evaluationDomain.removeListener('error', complete);
              const reply = {id: request.id, ok: true, error: null};
              try {
                if (error) throw error;
                if (value && typeof value.then === 'function') await value;
              } catch (failure) {
                reply.ok = false;
                reply.error = String(failure);
                console.error(failure && failure.stack || String(failure));
              } finally {
                const marker = '\x1eREMOTE_CONTROL_END_' + request.id + '\x1f';
                process.stdout.write(marker, () => process.stderr.write(marker, () => {
                  control.write(JSON.stringify(reply) + '\n');
                  resolve();
                }));
              }
            }
            evaluationDomain.once('error', complete);
            try { shell.eval(request.code, shell.context, '<remote>', complete); }
            catch (error) { complete(error); }
          });
        }
        lines.on('line', line => { chain = chain.then(() => execute(JSON.parse(line))); });
        control.on('error', error => { console.error(error); process.exit(1); });
        control.on('end', () => process.exit(0));
        """;
}
